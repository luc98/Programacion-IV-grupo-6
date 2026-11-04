using AvailabilitySite.Infrastructure.Outbox;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace AvailabilitySite.Infrastructure.Persistence.MongoDb.Documents;

// Colección OutboxMessages. _id = MessageId.
// Payload es el JSON del mensaje tal como se publica en el broker (ReservationRequestedMessage).
public sealed class OutboxMessageDocument
{
    [BsonId]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid MessageId { get; set; }

    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid RequestId { get; set; }

    public string MessageType { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? PublishedAt { get; set; }

    [BsonRepresentation(BsonType.String)]
    public OutboxMessageStatus Status { get; set; }

    public int RetryCount { get; set; }

    public string? LastError { get; set; }

    // A partir de cuándo se puede volver a intentar (backoff entre reintentos).
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime NextAttemptAt { get; set; }

    // Proceso que tiene reclamado el mensaje y hasta cuándo (control de concurrencia).
    public string? LockedBy { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LockedUntil { get; set; }
}
