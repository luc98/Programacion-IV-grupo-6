using AvailabilitySite.Infrastructure.Persistence.MongoDb;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace AvailabilitySite.Infrastructure.IntegrationTests;

[Collection(MongoCollection.Name)]
public sealed class MongoAvailabilityQueryRepositoryTests(MongoTestFixture fixture)
{
    [SkippableFact]
    public async Task GetSeatsAsync_ExistingEvent_ReturnsItsSeatsOnly()
    {
        await using var db = await fixture.CreateDatabaseAsync();
        var eventId = Guid.NewGuid();
        var seatA = Guid.NewGuid();
        var seatB = Guid.NewGuid();
        await db.SeedEventAsync(eventId, "Recital", (seatB, "A-02", false), (seatA, "A-01", true));
        await db.SeedEventAsync(Guid.NewGuid(), "Otro evento", (Guid.NewGuid(), "Z-01", true));

        var seats = await db.Availability.GetSeatsAsync(eventId);

        Assert.Equal(2, seats.Count);
        Assert.Equal(seatA, seats[0].Id);
        Assert.Equal("A-01", seats[0].Label);
        Assert.True(seats[0].IsAvailable);
        Assert.Equal(eventId, seats[0].EventId);
        Assert.Equal(seatB, seats[1].Id);
        Assert.False(seats[1].IsAvailable);
    }

    [SkippableFact]
    public async Task FindSeatsAsync_MissingEvent_ReturnsNull()
    {
        await using var db = await fixture.CreateDatabaseAsync();

        Assert.Null(await db.Availability.FindSeatsAsync(Guid.NewGuid()));
        Assert.False(await db.Availability.EventExistsAsync(Guid.NewGuid()));
        // El contrato actual de Application no distingue: devuelve una lista vacía.
        Assert.Empty(await db.Availability.GetSeatsAsync(Guid.NewGuid()));
    }

    [SkippableFact]
    public async Task FindSeatsAsync_EventWithoutSeats_ReturnsEmptyList()
    {
        await using var db = await fixture.CreateDatabaseAsync();
        var eventId = Guid.NewGuid();
        await db.SeedEventAsync(eventId, "Función sin asientos");

        var seats = await db.Availability.FindSeatsAsync(eventId);

        Assert.NotNull(seats);
        Assert.Empty(seats);
        Assert.True(await db.Availability.EventExistsAsync(eventId));
    }

    [SkippableFact]
    public async Task ListEventsAsync_ReturnsSummariesWithVenueAndDate()
    {
        await using var db = await fixture.CreateDatabaseAsync();
        var eventId = Guid.NewGuid();
        await db.SeedEventAsync(eventId, "Recital");

        var events = await db.Availability.ListEventsAsync();

        var summary = Assert.Single(events);
        Assert.Equal(eventId, summary.EventId);
        Assert.Equal("Teatro de prueba", summary.Venue);
        Assert.Equal(new DateTimeOffset(2027, 3, 12, 0, 0, 0, TimeSpan.Zero), summary.StartsAt);
        Assert.Equal(summary, await db.Availability.FindEventAsync(eventId));
    }

    [SkippableFact]
    public async Task EnsureIndexesAsync_CreatesRequiredIndexes()
    {
        await using var db = await fixture.CreateDatabaseAsync();

        // Se puede ejecutar más de una vez (por ejemplo, en cada arranque).
        await new MongoIndexInitializer(db.Context).EnsureIndexesAsync();

        Assert.Contains(MongoIndexInitializer.SeatsByEvent, await IndexNamesAsync(db.Context.Seats));
        var requestIndexes = await IndexesAsync(db.Context.ReservationRequests);
        Assert.True(requestIndexes[MongoIndexInitializer.RequestsByIdempotencyKey]["unique"].AsBoolean);
        Assert.Contains(MongoIndexInitializer.RequestsByUserAndDate, requestIndexes.Keys);
        var outboxIndexes = await IndexNamesAsync(db.Context.OutboxMessages);
        Assert.Contains(MongoIndexInitializer.OutboxByStatusAndDate, outboxIndexes);
        Assert.Contains(MongoIndexInitializer.OutboxByRequest, outboxIndexes);
    }

    private static async Task<IReadOnlyList<string>> IndexNamesAsync<T>(IMongoCollection<T> collection) =>
        (await IndexesAsync(collection)).Keys.ToList();

    private static async Task<Dictionary<string, BsonDocument>> IndexesAsync<T>(IMongoCollection<T> collection)
    {
        var indexes = await (await collection.Indexes.ListAsync()).ToListAsync();
        return indexes.ToDictionary(index => index["name"].AsString);
    }
}

