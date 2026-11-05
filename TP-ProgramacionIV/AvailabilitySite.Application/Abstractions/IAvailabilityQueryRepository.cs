using AvailabilitySite.Domain.Entities;

namespace AvailabilitySite.Application.Abstractions;

public interface IAvailabilityQueryRepository
{
    Task<IReadOnlyList<Events.EventSummaryDto>> ListEventsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Seat>> GetSeatsAsync(Guid eventId, CancellationToken cancellationToken = default);
}
