namespace AvailabilitySite.Application.Reservations;

public sealed record ReservationRequestReceiptDto(
    Guid RequestId,
    Guid EventId,
    Guid SeatId,
    DateTimeOffset ReceivedAt,
    bool IsReplay,
    ReservationRequestStatus Status);
