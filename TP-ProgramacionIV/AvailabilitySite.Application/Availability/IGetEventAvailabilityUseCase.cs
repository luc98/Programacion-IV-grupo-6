using AvailabilitySite.Application.Common;

namespace AvailabilitySite.Application.Availability;

public interface IGetEventAvailabilityUseCase
{
    Task<Result<EventAvailabilityDto>> ExecuteAsync(
        Guid eventId,
        CancellationToken cancellationToken = default);
}
