using AvailabilitySite.Application.Common;

namespace AvailabilitySite.Application.Reservations;

public interface IGetReservationRequestStatusUseCase
{
    Task<Result<ReservationTrackingDto>> ExecuteAsync(
        Guid requestId,
        CancellationToken cancellationToken = default);
}
