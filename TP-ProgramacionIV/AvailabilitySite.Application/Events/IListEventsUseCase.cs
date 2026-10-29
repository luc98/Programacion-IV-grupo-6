using AvailabilitySite.Application.Common;

namespace AvailabilitySite.Application.Events;

public interface IListEventsUseCase
{
    Task<Result<IReadOnlyList<EventSummaryDto>>> ExecuteAsync(
        CancellationToken cancellationToken = default);
}
