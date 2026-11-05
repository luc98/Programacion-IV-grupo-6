using AvailabilitySite.Application.Abstractions;
using AvailabilitySite.Application.Common;

namespace AvailabilitySite.Application.Events;

public sealed class ListEventsService(IAvailabilityQueryRepository availabilityQueryRepository)
    : IListEventsUseCase
{
    public async Task<Result<IReadOnlyList<EventSummaryDto>>> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var events = await availabilityQueryRepository.ListEventsAsync(cancellationToken);
        return Result<IReadOnlyList<EventSummaryDto>>.Success(events);
    }
}
