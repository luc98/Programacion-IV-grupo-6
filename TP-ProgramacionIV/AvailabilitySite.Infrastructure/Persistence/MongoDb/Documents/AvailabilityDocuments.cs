using AvailabilitySite.Application.Reservations;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace AvailabilitySite.Infrastructure.Persistence.MongoDb.Documents;

// Documentos de MongoDB. Son modelos de persistencia: no salen de Infrastructure.
// Los Guid se guardan con representación estándar (UUID) y las fechas en UTC.

// Colección Events. _id = EventId.
public sealed class EventDocument
{
    [BsonId]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid EventId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Venue { get; set; } = string.Empty;

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime StartsAt { get; set; }
}

// Colección Seats. _id = SeatId; cada asiento referencia a su evento por EventId.
// IsAvailable es solo la disponibilidad informativa que muestra Availability:
// el estado definitivo de un asiento vendido lo administra Booking.
public sealed class SeatDocument
{
    [BsonId]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid SeatId { get; set; }

    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid EventId { get; set; }

    public string Label { get; set; } = string.Empty;

    public bool IsAvailable { get; set; }
}

// Colección ReservationRequests. _id = RequestId (el mismo que viaja a Booking como SolicitudId).
// No guarda el estado de la reserva: ese lo informa Booking.
public sealed class ReservationRequestDocument
{
    [BsonId]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid RequestId { get; set; }

    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid EventId { get; set; }

    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid SeatId { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string IdempotencyKey { get; set; } = string.Empty;

    [BsonRepresentation(BsonType.String)]
    public ReservationRequestStatus RequestStatus { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; }
}
