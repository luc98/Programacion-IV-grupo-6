namespace AvailabilitySite.Application.Reservations;

// Modelo de aplicación para registrar la solicitud antes de su publicación.
public sealed record ReservationRequestRecord(
    Guid RequestId,
    Guid EventId,
    Guid SeatId,
    string UserId,
    string IdempotencyKey,
    DateTimeOffset ReceivedAt,
    ReservationRequestStatus Status);
