using AvailabilitySite.Application.Reservations;
using AvailabilitySite.Infrastructure.Outbox;
using AvailabilitySite.Infrastructure.Persistence.MongoDb;
using MongoDB.Driver;
using Xunit;

namespace AvailabilitySite.Infrastructure.IntegrationTests;

[Collection(MongoCollection.Name)]
public sealed class MongoOutboxRepositoryTests(MongoTestFixture fixture)
{
    private static readonly OutboxOptions Options = new()
    {
        MaxAttempts = 3,
        LeaseDuration = TimeSpan.FromSeconds(30),
        RetryBaseDelay = TimeSpan.FromSeconds(10),
        MaxRetryDelay = TimeSpan.FromMinutes(1),
    };

    [SkippableFact]
    public async Task GetPendingAsync_ReturnsMessagesSavedWithNewRequests()
    {
        var clock = new ManualClock();
        await using var db = await fixture.CreateDatabaseAsync(Options, clock);
        var first = await AddRequestAsync(db, clock.Now.AddSeconds(-2));
        var second = await AddRequestAsync(db, clock.Now.AddSeconds(-1));

        var pending = await db.Outbox.GetPendingAsync(10);

        Assert.Equal([first, second], pending.Select(m => m.RequestId));
        Assert.All(pending, m => Assert.Equal(OutboxMessageStatus.Pending, m.Status));
    }

    [SkippableFact]
    public async Task ClaimThenMarkPublished_MessageIsNoLongerPending()
    {
        var clock = new ManualClock();
        await using var db = await fixture.CreateDatabaseAsync(Options, clock);
        await AddRequestAsync(db, clock.Now);

        var claimed = await db.Outbox.TryClaimNextAsync("worker-1");
        Assert.NotNull(claimed);
        Assert.Equal(OutboxMessageStatus.Processing, claimed.Status);
        Assert.Equal("worker-1", claimed.LockedBy);
        Assert.Empty(await db.Outbox.GetPendingAsync(10));

        Assert.True(await db.Outbox.MarkPublishedAsync(claimed.MessageId, "worker-1"));

        var stored = await db.Outbox.FindByIdAsync(claimed.MessageId);
        Assert.Equal(OutboxMessageStatus.Published, stored!.Status);
        Assert.Equal(clock.Now, stored.PublishedAt);
        Assert.Null(stored.LockedBy);
        Assert.Null(await db.Outbox.TryClaimNextAsync("worker-1"));
    }

    [SkippableFact]
    public async Task MarkPublishedAsync_ByAnotherProcess_IsRejected()
    {
        var clock = new ManualClock();
        await using var db = await fixture.CreateDatabaseAsync(Options, clock);
        await AddRequestAsync(db, clock.Now);
        var claimed = await db.Outbox.TryClaimNextAsync("worker-1");

        Assert.False(await db.Outbox.MarkPublishedAsync(claimed!.MessageId, "worker-2"));
        Assert.Equal(OutboxMessageStatus.Processing, (await db.Outbox.FindByIdAsync(claimed.MessageId))!.Status);
    }

    [SkippableFact]
    public async Task ConcurrentClaims_EachMessageIsClaimedByOneProcessOnly()
    {
        var clock = new ManualClock();
        await using var db = await fixture.CreateDatabaseAsync(Options, clock);
        for (var i = 0; i < 5; i++)
        {
            await AddRequestAsync(db, clock.Now.AddMilliseconds(-i));
        }

        var claims = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => db.Outbox.TryClaimNextAsync($"worker-{i}")));

        var claimed = claims.Where(c => c is not null).ToList();
        Assert.Equal(5, claimed.Count);
        Assert.Equal(5, claimed.Select(c => c!.MessageId).Distinct().Count());
    }

    [SkippableFact]
    public async Task MarkFailedAsync_RecordsErrorAndSchedulesRetryWithBackoff()
    {
        var clock = new ManualClock();
        await using var db = await fixture.CreateDatabaseAsync(Options, clock);
        await AddRequestAsync(db, clock.Now);
        var claimed = await db.Outbox.TryClaimNextAsync("worker-1");

        var result = await db.Outbox.MarkFailedAsync(claimed!.MessageId, "worker-1", "Service Bus no respondió");

        Assert.True(result.Updated);
        Assert.False(result.MovedToFailed);
        Assert.Equal(1, result.RetryCount);
        var stored = await db.Outbox.FindByIdAsync(claimed.MessageId);
        Assert.Equal(OutboxMessageStatus.Pending, stored!.Status);
        Assert.Equal("Service Bus no respondió", stored.LastError);
        Assert.Equal(clock.Now + Options.RetryBaseDelay, stored.NextAttemptAt);

        // Antes de que pase la espera no se vuelve a entregar; después sí.
        Assert.Null(await db.Outbox.TryClaimNextAsync("worker-1"));
        clock.Advance(Options.RetryBaseDelay);
        Assert.Equal(claimed.MessageId, (await db.Outbox.TryClaimNextAsync("worker-1"))?.MessageId);
    }

    [SkippableFact]
    public async Task MarkFailedAsync_AfterMaxAttempts_MovesToFailedUntilRequeued()
    {
        var clock = new ManualClock();
        await using var db = await fixture.CreateDatabaseAsync(Options, clock);
        await AddRequestAsync(db, clock.Now);

        OutboxFailureResult? last = null;
        for (var attempt = 0; attempt < Options.MaxAttempts; attempt++)
        {
            var claimed = await db.Outbox.TryClaimNextAsync("worker-1");
            Assert.NotNull(claimed);
            last = await db.Outbox.MarkFailedAsync(claimed.MessageId, "worker-1", $"error {attempt}");
            clock.Advance(Options.MaxRetryDelay);
        }

        Assert.True(last!.MovedToFailed);
        Assert.Null(await db.Outbox.TryClaimNextAsync("worker-1"));

        var failed = await db.Context.OutboxMessages.Find(m => m.Status == OutboxMessageStatus.Failed).SingleAsync();
        Assert.Equal(Options.MaxAttempts, failed.RetryCount);
        Assert.True(await db.Outbox.RequeueAsync(failed.MessageId));
        Assert.Equal(failed.MessageId, (await db.Outbox.TryClaimNextAsync("worker-1"))?.MessageId);
    }

    [SkippableFact]
    public async Task ExpiredLease_AfterProcessCrash_MessageCanBeReclaimed()
    {
        var clock = new ManualClock();
        await using var db = await fixture.CreateDatabaseAsync(Options, clock);
        await AddRequestAsync(db, clock.Now);
        var claimed = await db.Outbox.TryClaimNextAsync("worker-que-se-cayo");

        Assert.Null(await db.Outbox.TryClaimNextAsync("worker-2"));
        clock.Advance(Options.LeaseDuration + TimeSpan.FromSeconds(1));

        var reclaimed = await db.Outbox.TryClaimNextAsync("worker-2");
        Assert.Equal(claimed!.MessageId, reclaimed?.MessageId);
        Assert.Equal("worker-2", reclaimed!.LockedBy);
        // El proceso original ya no puede marcarlo.
        Assert.False(await db.Outbox.MarkPublishedAsync(claimed.MessageId, "worker-que-se-cayo"));
    }

    private static async Task<Guid> AddRequestAsync(TestDatabase db, DateTimeOffset receivedAt)
    {
        var request = new ReservationRequestRecord(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "user-1", Guid.NewGuid().ToString(), receivedAt, ReservationRequestStatus.Pending);
        await db.Requests.AddAsync(request);
        return request.RequestId;
    }

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; private set; } = new(2027, 1, 1, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;

        public void Advance(TimeSpan delta) => Now += delta;
    }
}

