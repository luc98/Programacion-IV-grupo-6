namespace AvailabilitySite.Application.Reservations;

public sealed record ReservationRequestSummaryDto(
    Guid RequestId,
    Guid EventId,
    Guid SeatId,
    ReservationRequestStatus RequestStatus,
    string? BookingStatus,
    DateTimeOffset CreatedAt);
