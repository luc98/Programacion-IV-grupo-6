namespace AvailabilitySite.Application.Events;

public sealed record EventSummaryDto(
    Guid EventId,
    string Name,
    string Venue,
    DateTimeOffset StartsAt);
