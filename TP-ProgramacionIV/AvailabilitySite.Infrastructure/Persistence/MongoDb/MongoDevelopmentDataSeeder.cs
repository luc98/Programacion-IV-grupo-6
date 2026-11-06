using AvailabilitySite.Infrastructure.Persistence.MongoDb.Documents;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace AvailabilitySite.Infrastructure.Persistence.MongoDb;

// Carga datos de ejemplo únicamente en Development. Cada documento se inserta con un
// upsert atómico y $setOnInsert, por lo que una ejecución repetida o concurrente no
// sobrescribe información existente ni depende de un conteo previo de la colección.
public sealed class MongoDevelopmentDataSeeder(
    MongoDbContext context,
    ILogger<MongoDevelopmentDataSeeder> logger)
{
    private static readonly Guid EventOneId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EventTwoId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly EventDocument[] SampleEvents =
    [
        new()
        {
            EventId = EventOneId,
            Name = "Conferencia de prueba",
            Venue = "Auditorio Central",
            StartsAt = new DateTime(2026, 12, 10, 22, 0, 0, DateTimeKind.Utc),
        },
        new()
        {
            EventId = EventTwoId,
            Name = "Recital de prueba",
            Venue = "Estadio Principal",
            StartsAt = new DateTime(2026, 12, 18, 23, 30, 0, DateTimeKind.Utc),
        },
    ];

    private static readonly SeatDocument[] SampleSeats =
    [
        new() { SeatId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1"), EventId = EventOneId, Label = "A-01", IsAvailable = true },
        new() { SeatId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2"), EventId = EventOneId, Label = "A-02", IsAvailable = true },
        new() { SeatId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3"), EventId = EventOneId, Label = "A-03", IsAvailable = true },
        new() { SeatId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa4"), EventId = EventOneId, Label = "A-04", IsAvailable = true },
        new() { SeatId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1"), EventId = EventTwoId, Label = "B-01", IsAvailable = true },
        new() { SeatId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb2"), EventId = EventTwoId, Label = "B-02", IsAvailable = true },
    ];

    public Task SeedAsync(CancellationToken cancellationToken = default) =>
        MongoErrors.ExecuteAsync(async () =>
        {
            foreach (var sampleEvent in SampleEvents)
            {
                await UpsertEventAsync(sampleEvent, cancellationToken);
            }

            foreach (var sampleSeat in SampleSeats)
            {
                await UpsertSeatAsync(sampleSeat, cancellationToken);
            }
        });

    private async Task UpsertEventAsync(EventDocument sample, CancellationToken cancellationToken)
    {
        var filter = Builders<EventDocument>.Filter.Eq(document => document.EventId, sample.EventId);
        var update = Builders<EventDocument>.Update
            .SetOnInsert(document => document.EventId, sample.EventId)
            .SetOnInsert(document => document.Name, sample.Name)
            .SetOnInsert(document => document.Venue, sample.Venue)
            .SetOnInsert(document => document.StartsAt, sample.StartsAt);

        var result = await context.Events.UpdateOneAsync(
            filter,
            update,
            new UpdateOptions { IsUpsert = true },
            cancellationToken);

        if (result.MatchedCount > 0)
        {
            var existing = await context.Events.Find(filter).FirstOrDefaultAsync(cancellationToken);
            if (existing is not null && !SameEvent(existing, sample))
            {
                logger.LogWarning(
                    "El evento de ejemplo {EventId} ya existe con datos diferentes; se conserva el documento existente.",
                    sample.EventId);
            }
        }
    }

    private async Task UpsertSeatAsync(SeatDocument sample, CancellationToken cancellationToken)
    {
        var filter = Builders<SeatDocument>.Filter.Eq(document => document.SeatId, sample.SeatId);
        var update = Builders<SeatDocument>.Update
            .SetOnInsert(document => document.SeatId, sample.SeatId)
            .SetOnInsert(document => document.EventId, sample.EventId)
            .SetOnInsert(document => document.Label, sample.Label)
            .SetOnInsert(document => document.IsAvailable, sample.IsAvailable);

        var result = await context.Seats.UpdateOneAsync(
            filter,
            update,
            new UpdateOptions { IsUpsert = true },
            cancellationToken);

        if (result.MatchedCount > 0)
        {
            var existing = await context.Seats.Find(filter).FirstOrDefaultAsync(cancellationToken);
            if (existing is not null && !SameSeat(existing, sample))
            {
                logger.LogWarning(
                    "El asiento de ejemplo {SeatId} ya existe con datos diferentes; se conserva el documento existente.",
                    sample.SeatId);
            }
        }
    }

    private static bool SameEvent(EventDocument left, EventDocument right) =>
        left.EventId == right.EventId &&
        left.Name == right.Name &&
        left.Venue == right.Venue &&
        left.StartsAt.ToUniversalTime() == right.StartsAt.ToUniversalTime();

    private static bool SameSeat(SeatDocument left, SeatDocument right) =>
        left.SeatId == right.SeatId &&
        left.EventId == right.EventId &&
        left.Label == right.Label &&
        left.IsAvailable == right.IsAvailable;
}

internal sealed class MongoDevelopmentDataSeederHostedService(MongoDevelopmentDataSeeder seeder) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => seeder.SeedAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
