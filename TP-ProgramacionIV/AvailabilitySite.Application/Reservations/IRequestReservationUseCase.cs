using AvailabilitySite.Application.Common;

namespace AvailabilitySite.Application.Reservations;

public interface IRequestReservationUseCase
{
    Task<Result<ReservationRequestReceiptDto>> ExecuteAsync(
        CreateReservationRequestDto request,
        CancellationToken cancellationToken = default);
}
