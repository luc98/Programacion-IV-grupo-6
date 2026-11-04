using AvailabilitySite.Application.Abstractions;
using AvailabilitySite.Application.Common;

namespace AvailabilitySite.Application.Reservations;

public sealed class ListCurrentUserReservationRequestsService(
    ICurrentUserContext currentUserContext,
    IReservationRequestRepository reservationRequestRepository)
    : IListCurrentUserReservationRequestsUseCase
{
    public async Task<Result<IReadOnlyList<ReservationRequestSummaryDto>>> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        if (!currentUserContext.IsAuthenticated || string.IsNullOrWhiteSpace(currentUserContext.UserId))
        {
            return Result<IReadOnlyList<ReservationRequestSummaryDto>>.Failure(
                new ApplicationError(
                    ApplicationErrorType.Unauthenticated,
                    "user_required",
                    "Se requiere un usuario autenticado."));
        }

        var requests = await reservationRequestRepository.ListByUserAsync(
            currentUserContext.UserId,
            cancellationToken: cancellationToken);

        var summaries = requests
            .Select(request => new ReservationRequestSummaryDto(
                request.RequestId,
                request.EventId,
                request.SeatId,
                request.Status,
                BookingStatus: null,
                request.ReceivedAt))
            .ToList();

        return Result<IReadOnlyList<ReservationRequestSummaryDto>>.Success(summaries);
    }
}
