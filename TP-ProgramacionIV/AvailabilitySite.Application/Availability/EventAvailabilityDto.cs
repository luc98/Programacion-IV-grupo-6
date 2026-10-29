namespace AvailabilitySite.Application.Availability;

public sealed record EventAvailabilityDto(
    Guid EventId,
    IReadOnlyList<SeatAvailabilityDto> Seats);

public sealed record SeatAvailabilityDto(
    Guid SeatId,
    string Label,
    bool IsAvailable);
