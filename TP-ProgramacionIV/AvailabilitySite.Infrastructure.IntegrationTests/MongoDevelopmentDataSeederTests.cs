using AvailabilitySite.Infrastructure.Persistence.MongoDb;
using AvailabilitySite.Infrastructure.Persistence.MongoDb.Documents;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Xunit;

namespace AvailabilitySite.Infrastructure.IntegrationTests;

[Collection(MongoCollection.Name)]
public sealed class MongoDevelopmentDataSeederTests(MongoTestFixture fixture)
{
    private static readonly Guid EventOneId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EventTwoId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SeatA01Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
    private static readonly Guid SeatB01Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1");

    [SkippableFact]
    public async Task SeedAsync_EmptyCollections_CreatesEventsAndSeatsWithCorrectRelations()
    {
        Skip.If(fixture.SkipReason is not null, fixture.SkipReason);
        await using var database = await fixture.CreateDatabaseAsync();

        await CreateSeeder(database).SeedAsync();

        var events = await database.Context.Events.Find(FilterDefinition<EventDocument>.Empty).ToListAsync();
        var seats = await database.Context.Seats.Find(FilterDefinition<SeatDocument>.Empty).ToListAsync();

        Assert.Equal(2, events.Count);
        Assert.Equal(6, seats.Count);
        Assert.Equal(4, seats.Count(seat => seat.EventId == EventOneId));
        Assert.Equal(2, seats.Count(seat => seat.EventId == EventTwoId));
        Assert.All(seats, seat => Assert.Contains(seat.EventId, new[] { EventOneId, EventTwoId }));
    }

    [SkippableFact]
    public async Task SeedAsync_RepeatedExecution_IsIdempotentAndDoesNotOverwriteExistingDocuments()
    {
        Skip.If(fixture.SkipReason is not null, fixture.SkipReason);
        await using var database = await fixture.CreateDatabaseAsync();
        var seeder = CreateSeeder(database);

        await seeder.SeedAsync();
        await database.Context.Events.UpdateOneAsync(
            eventDocument => eventDocument.EventId == EventOneId,
            Builders<EventDocument>.Update.Set(eventDocument => eventDocument.Name, "Nombre conservado"));
        await database.Context.Seats.UpdateOneAsync(
            seat => seat.SeatId == SeatA01Id,
            Builders<SeatDocument>.Update.Set(seat => seat.Label, "Etiqueta conservada"));

        await seeder.SeedAsync();
        await seeder.SeedAsync();

        Assert.Equal(2, await database.Context.Events.CountDocumentsAsync(FilterDefinition<EventDocument>.Empty));
        Assert.Equal(6, await database.Context.Seats.CountDocumentsAsync(FilterDefinition<SeatDocument>.Empty));
        Assert.Equal("Nombre conservado", (await database.Context.Events.Find(e => e.EventId == EventOneId).FirstAsync()).Name);
        Assert.Equal("Etiqueta conservada", (await database.Context.Seats.Find(s => s.SeatId == SeatA01Id).FirstAsync()).Label);
    }

    [SkippableFact]
    public async Task SeedAsync_PartiallyPopulatedCollections_OnlyAddsMissingDocuments()
    {
        Skip.If(fixture.SkipReason is not null, fixture.SkipReason);
        await using var database = await fixture.CreateDatabaseAsync();

        await database.Context.Events.InsertOneAsync(new EventDocument
        {
            EventId = EventOneId,
            Name = "Evento existente",
            Venue = "Lugar existente",
            StartsAt = DateTime.UtcNow,
        });
        await database.Context.Seats.InsertManyAsync(
        [
            new SeatDocument { SeatId = SeatA01Id, EventId = EventOneId, Label = "Asiento existente", IsAvailable = false },
            new SeatDocument { SeatId = SeatB01Id, EventId = EventTwoId, Label = "B-01 existente", IsAvailable = false },
        ]);

        await CreateSeeder(database).SeedAsync();

        Assert.Equal(2, await database.Context.Events.CountDocumentsAsync(FilterDefinition<EventDocument>.Empty));
        Assert.Equal(6, await database.Context.Seats.CountDocumentsAsync(FilterDefinition<SeatDocument>.Empty));
        Assert.Equal("Evento existente", (await database.Context.Events.Find(e => e.EventId == EventOneId).FirstAsync()).Name);
        Assert.False((await database.Context.Seats.Find(s => s.SeatId == SeatA01Id).FirstAsync()).IsAvailable);
        Assert.False((await database.Context.Seats.Find(s => s.SeatId == SeatB01Id).FirstAsync()).IsAvailable);
    }

    [SkippableFact]
    public async Task SeedAsync_ConcurrentExecutions_DoNotCreateDuplicates()
    {
        Skip.If(fixture.SkipReason is not null, fixture.SkipReason);
        await using var database = await fixture.CreateDatabaseAsync();
        var seeder = CreateSeeder(database);

        await Task.WhenAll(seeder.SeedAsync(), seeder.SeedAsync());

        Assert.Equal(2, await database.Context.Events.CountDocumentsAsync(FilterDefinition<EventDocument>.Empty));
        Assert.Equal(6, await database.Context.Seats.CountDocumentsAsync(FilterDefinition<SeatDocument>.Empty));
    }

    private static MongoDevelopmentDataSeeder CreateSeeder(TestDatabase database) =>
        new(database.Context, NullLogger<MongoDevelopmentDataSeeder>.Instance);
}
