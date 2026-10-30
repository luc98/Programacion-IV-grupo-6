using AvailabilitySite.Application.Abstractions;
using AvailabilitySite.Application.Common;

namespace AvailabilitySite.Application.Reservations;

public sealed class GetReservationRequestStatusService(IReservationStatusClient reservationStatusClient)
    : IGetReservationRequestStatusUseCase
{
    public Task<Result<ReservationTrackingDto>> ExecuteAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        if (requestId == Guid.Empty)
        {
            return Task.FromResult(Result<ReservationTrackingDto>.Failure(
                new ApplicationError(ApplicationErrorType.Validation, "request_id_required", "El identificador de la solicitud es obligatorio.")));
        }

        return reservationStatusClient.GetAsync(requestId, cancellationToken);
    }
}
