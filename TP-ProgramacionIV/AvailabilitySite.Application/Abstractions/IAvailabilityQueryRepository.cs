using AvailabilitySite.Domain.Entities;

namespace AvailabilitySite.Application.Abstractions;

public interface IAvailabilityQueryRepository
{
    Task<IReadOnlyList<Seat>> GetSeatsAsync(Guid eventId, CancellationToken cancellationToken = default);
}
