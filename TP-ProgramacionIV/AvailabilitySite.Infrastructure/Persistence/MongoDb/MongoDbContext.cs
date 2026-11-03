using AvailabilitySite.Infrastructure.Persistence.MongoDb.Documents;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace AvailabilitySite.Infrastructure.Persistence.MongoDb;

// Acceso a la base AvailabilityDb y sus colecciones. Es thread-safe y se registra como singleton.
public sealed class MongoDbContext
{
    public const string EventsCollection = "Events";
    public const string SeatsCollection = "Seats";
    public const string ReservationRequestsCollection = "ReservationRequests";
    public const string OutboxMessagesCollection = "OutboxMessages";

    public MongoDbContext(IMongoClient client, IOptions<MongoDbOptions> options)
    {
        Client = client;
        Database = client.GetDatabase(options.Value.DatabaseName);
        Events = Database.GetCollection<EventDocument>(EventsCollection);
        Seats = Database.GetCollection<SeatDocument>(SeatsCollection);
        ReservationRequests = Database.GetCollection<ReservationRequestDocument>(ReservationRequestsCollection);
        OutboxMessages = Database.GetCollection<OutboxMessageDocument>(OutboxMessagesCollection);
    }

    public IMongoClient Client { get; }

    public IMongoDatabase Database { get; }

    public IMongoCollection<EventDocument> Events { get; }

    public IMongoCollection<SeatDocument> Seats { get; }

    public IMongoCollection<ReservationRequestDocument> ReservationRequests { get; }

    public IMongoCollection<OutboxMessageDocument> OutboxMessages { get; }
}

