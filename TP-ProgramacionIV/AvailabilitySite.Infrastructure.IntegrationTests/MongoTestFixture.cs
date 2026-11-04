using AvailabilitySite.Infrastructure.Outbox;
using AvailabilitySite.Infrastructure.Persistence.MongoDb;
using AvailabilitySite.Infrastructure.Persistence.MongoDb.Documents;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Testcontainers.MongoDb;
using Xunit;

namespace AvailabilitySite.Infrastructure.IntegrationTests;

// Instancia de MongoDB para las pruebas, separada de la base de desarrollo:
// - Si existe AVAILABILITY_MONGODB_TEST_CONNECTION_STRING, se usa esa (por ejemplo, un cluster de prueba en Atlas).
// - Si no, se levanta un contenedor de MongoDB con replica set (necesario para transacciones) usando Docker.
// - Si no hay ninguna de las dos, las pruebas se informan como omitidas.
// Cada clase de pruebas usa su propia base con nombre aleatorio y la borra al terminar.
public sealed class MongoTestFixture : IAsyncLifetime
{
    public const string ConnectionStringVariable = "AVAILABILITY_MONGODB_TEST_CONNECTION_STRING";

    private MongoDbContainer? _container;

    public string? ConnectionString { get; private set; }

    public string? SkipReason { get; private set; }

    public async Task InitializeAsync()
    {
        ConnectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (!string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }

        try
        {
            _container = new MongoDbBuilder("mongo:7.0")
                .WithReplicaSet()
                .Build();
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
        }
        catch (Exception exception)
        {
            SkipReason = $"No hay MongoDB para pruebas: definí {ConnectionStringVariable} o iniciá Docker. ({exception.GetType().Name})";
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    public async Task<TestDatabase> CreateDatabaseAsync(OutboxOptions? outboxOptions = null, TimeProvider? timeProvider = null)
    {
        Skip.If(SkipReason is not null, SkipReason);

        var options = Options.Create(new MongoDbOptions
        {
            ConnectionString = ConnectionString!,
            DatabaseName = $"AvailabilityDb_Tests_{Guid.NewGuid():N}",
        });
        var context = new MongoDbContext(new MongoClient(options.Value.ConnectionString), options);
        await new MongoIndexInitializer(context).EnsureIndexesAsync();
        return new TestDatabase(context, Options.Create(outboxOptions ?? new OutboxOptions()), timeProvider ?? TimeProvider.System);
    }
}

[CollectionDefinition(Name)]
public sealed class MongoCollection : ICollectionFixture<MongoTestFixture>
{
    public const string Name = "MongoDB";
}

public sealed class TestDatabase(MongoDbContext context, IOptions<OutboxOptions> outboxOptions, TimeProvider timeProvider) : IAsyncDisposable
{
    public MongoDbContext Context { get; } = context;

    public MongoAvailabilityQueryRepository Availability => new(Context);

    public MongoReservationRequestRepository Requests => new(Context);

    public MongoOutboxRepository Outbox => new(Context, outboxOptions, timeProvider);

    public async Task SeedEventAsync(Guid eventId, string name, params (Guid Id, string Label, bool IsAvailable)[] seats)
    {
        await Context.Events.InsertOneAsync(new EventDocument
        {
            EventId = eventId,
            Name = name,
            Venue = "Teatro de prueba",
            StartsAt = new DateTime(2027, 3, 12, 0, 0, 0, DateTimeKind.Utc),
        });

        if (seats.Length > 0)
        {
            await Context.Seats.InsertManyAsync(seats.Select(seat => new SeatDocument
            {
                SeatId = seat.Id,
                EventId = eventId,
                Label = seat.Label,
                IsAvailable = seat.IsAvailable,
            }));
        }
    }

    public async ValueTask DisposeAsync() => await Context.Client.DropDatabaseAsync(Context.Database.DatabaseNamespace.DatabaseName);
}
