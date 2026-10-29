namespace AvailabilitySite.Application.Reservations;

public sealed record CreateReservationRequestDto(
    Guid EventId,
    Guid SeatId,
    string IdempotencyKey);
