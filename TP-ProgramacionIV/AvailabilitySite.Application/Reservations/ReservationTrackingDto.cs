namespace AvailabilitySite.Application.Reservations;

public sealed record ReservationTrackingDto(
    Guid ReservaId,
    Guid RequestId,
    Guid EventId,
    Guid SeatId,
    ReservationRequestStatus RequestStatus,
    string? BookingStatus,
    string? RejectionReason,
    DateTime CreatedAtUtc,
    DateTime ExpirationAtUtc);
