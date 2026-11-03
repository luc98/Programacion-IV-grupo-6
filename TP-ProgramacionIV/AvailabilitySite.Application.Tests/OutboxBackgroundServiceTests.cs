using AvailabilitySite.Application.Abstractions;
using AvailabilitySite.Application.Reservations;
using AvailabilitySite.Infrastructure.Outbox;
using AvailabilitySite.Infrastructure.Persistence.MongoDb;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AvailabilitySite.Application.Tests;

public sealed class OutboxBackgroundServiceTests
{
    [Fact]
    public async Task ProcessOneAsync_PublishesAndMarksPublished()
    {
        var outbox = new FakeOutbox(new OutboxMessage(Guid.NewGuid(), Guid.NewGuid(), "ReservationRequested",
            Payload(), DateTimeOffset.UtcNow, null, OutboxMessageStatus.Pending, 0, null, DateTimeOffset.UtcNow, null, null));
        var publisher = new FakePublisher();
        await using var provider = Provider(outbox, publisher);
        var worker = Worker(provider, TimeSpan.FromSeconds(1));

        Assert.True(await worker.ProcessOneAsync(CancellationToken.None));
        Assert.NotNull(publisher.Message);
        Assert.True(outbox.Published);
    }

    [Fact]
    public async Task ProcessOneAsync_PublisherError_MarksFailedAndNotPublished()
    {
        var outbox = new FakeOutbox(Message());
        var publisher = new FakePublisher(new InvalidOperationException("failure"));
        await using var provider = Provider(outbox, publisher);

        await Worker(provider).ProcessOneAsync(CancellationToken.None);

        Assert.False(outbox.Published);
        Assert.Equal(1, outbox.Failed);
    }

    [Fact]
    public async Task ProcessOneAsync_CorruptPayload_MarksFailed()
    {
        var message = Message() with { Payload = "not-json" };
        var outbox = new FakeOutbox(message);
        await using var provider = Provider(outbox, new FakePublisher());

        await Worker(provider).ProcessOneAsync(CancellationToken.None);

        Assert.False(outbox.Published);
        Assert.Equal(1, outbox.Failed);
    }

    [Fact]
    public async Task ProcessOneAsync_RenewsLeaseWhilePublisherIsRunning()
    {
        var outbox = new FakeOutbox(Message());
        var publisher = new FakePublisher(delay: TimeSpan.FromMilliseconds(700));
        await using var provider = Provider(outbox, publisher);
        var worker = new OutboxBackgroundService(provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OutboxBackgroundService>.Instance,
            Options.Create(new OutboxOptions { LeaseDuration = TimeSpan.FromSeconds(1) }));

        await worker.ProcessOneAsync(CancellationToken.None);

        Assert.True(outbox.Renewals > 0);
        Assert.True(outbox.Published);
    }

    [Fact]
    public async Task ProcessOneAsync_WhenLeaseIsLost_DoesNotMarkPublished()
    {
        var outbox = new FakeOutbox(Message()) { RenewResult = false };
        await using var provider = Provider(outbox, new FakePublisher(delay: TimeSpan.FromMilliseconds(700)));
        var worker = Worker(provider, TimeSpan.FromSeconds(1));

        await worker.ProcessOneAsync(CancellationToken.None);

        Assert.False(outbox.Published);
        Assert.Equal(0, outbox.Failed);
    }

    [Fact]
    public async Task ProcessOneAsync_WhenRenewalFails_DoesNotContinueAsOwner()
    {
        var outbox = new FakeOutbox(Message()) { RenewalError = new InvalidOperationException("mongo unavailable") };
        await using var provider = Provider(outbox, new FakePublisher(delay: TimeSpan.FromMilliseconds(700)));
        var worker = Worker(provider, TimeSpan.FromSeconds(1));

        await worker.ProcessOneAsync(CancellationToken.None);

        Assert.False(outbox.Published);
        Assert.Equal(0, outbox.Failed);
    }

    [Fact]
    public async Task ProcessOneAsync_WhenMarkPublishedReturnsFalse_DoesNotMarkFailed()
    {
        var outbox = new FakeOutbox(Message()) { MarkPublishedResult = false };
        await using var provider = Provider(outbox, new FakePublisher());

        await Worker(provider).ProcessOneAsync(CancellationToken.None);

        Assert.False(outbox.Published);
        Assert.Equal(0, outbox.Failed);
    }

    [Fact]
    public async Task ProcessOneAsync_WaitsForPublisherThatIgnoresCancellation()
    {
        var outbox = new FakeOutbox(Message()) { RenewResult = false };
        await using var provider = Provider(outbox, new FakePublisher(delay: TimeSpan.FromMilliseconds(700), ignoreCancellation: true));
        var worker = Worker(provider, TimeSpan.FromSeconds(1));

        await worker.ProcessOneAsync(CancellationToken.None);

        Assert.False(outbox.Published);
    }

    private static OutboxBackgroundService Worker(ServiceProvider provider, TimeSpan? leaseDuration = null) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<OutboxBackgroundService>.Instance,
            Options.Create(new OutboxOptions { LeaseDuration = leaseDuration ?? TimeSpan.FromSeconds(30) }));

    private static ServiceProvider Provider(FakeOutbox outbox, FakePublisher publisher) =>
        new ServiceCollection()
            .AddScoped<IOutboxRepository>(_ => outbox)
            .AddScoped<IReservationRequestPublisher>(_ => publisher)
            .BuildServiceProvider();

    private static OutboxMessage Message() => new(Guid.NewGuid(), Guid.NewGuid(), "ReservationRequested",
        Payload(), DateTimeOffset.UtcNow, null, OutboxMessageStatus.Pending, 0, null, DateTimeOffset.UtcNow, null, null);

    private static string Payload() => System.Text.Json.JsonSerializer.Serialize(
        new ReservationRequestedMessage(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "user-1"));

    private sealed class FakePublisher(Exception? error = null, TimeSpan? delay = null, bool ignoreCancellation = false) : IReservationRequestPublisher
    {
        public ReservationRequestedMessage? Message { get; private set; }
        public Task PublishAsync(ReservationRequestedMessage message, CancellationToken cancellationToken = default)
        {
            if (error is not null) return Task.FromException(error);
            if (delay is { } wait) return PublishAfterDelayAsync(message, wait, ignoreCancellation ? CancellationToken.None : cancellationToken);
            Message = message;
            return Task.CompletedTask;
        }

        private async Task PublishAfterDelayAsync(ReservationRequestedMessage message, TimeSpan wait, CancellationToken cancellationToken)
        {
            await Task.Delay(wait, cancellationToken);
            Message = message;
        }
    }

    private sealed class FakeOutbox(OutboxMessage? pending) : IOutboxRepository
    {
        public bool Published { get; private set; }
        public int Failed { get; private set; }
        public int Renewals { get; private set; }
        public bool RenewResult { get; init; } = true;
        public Exception? RenewalError { get; init; }
        public bool MarkPublishedResult { get; init; } = true;
        public Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<OutboxMessage>>([]);
        public Task<OutboxMessage?> TryClaimNextAsync(string processorId, CancellationToken cancellationToken = default) => Task.FromResult(pending);
        public Task<IReadOnlyList<OutboxMessage>> ClaimBatchAsync(string processorId, int batchSize, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<OutboxMessage>>([]);
        public Task<bool> MarkPublishedAsync(Guid messageId, string processorId, CancellationToken cancellationToken = default) { Published = MarkPublishedResult; return Task.FromResult(MarkPublishedResult); }
        public Task<bool> RenewLeaseAsync(Guid messageId, string processorId, TimeSpan leaseDuration, CancellationToken cancellationToken = default) { Renewals++; return RenewalError is null ? Task.FromResult(RenewResult) : Task.FromException<bool>(RenewalError); }
        public Task<OutboxFailureResult> MarkFailedAsync(Guid messageId, string processorId, string error, CancellationToken cancellationToken = default) { Failed++; return Task.FromResult(new OutboxFailureResult(true, false, 1, DateTimeOffset.UtcNow)); }
        public Task<bool> RequeueAsync(Guid messageId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<OutboxMessage?> FindByIdAsync(Guid messageId, CancellationToken cancellationToken = default) => Task.FromResult<OutboxMessage?>(pending);
    }
}
