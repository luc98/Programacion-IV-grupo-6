using AvailabilitySite.Application.Common;

namespace AvailabilitySite.Application.Reservations;

public interface IListCurrentUserReservationRequestsUseCase
{
    Task<Result<IReadOnlyList<ReservationRequestSummaryDto>>> ExecuteAsync(
        CancellationToken cancellationToken = default);
}
