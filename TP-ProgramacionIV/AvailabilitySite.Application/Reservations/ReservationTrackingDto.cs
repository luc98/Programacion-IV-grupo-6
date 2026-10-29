namespace AvailabilitySite.Application.Reservations;

public sealed record ReservationTrackingDto(
    Guid RequestId,
    Guid EventId,
    Guid SeatId,
    ReservationRequestStatus RequestStatus,
    string? BookingStatus,
    string? RejectionReason);
