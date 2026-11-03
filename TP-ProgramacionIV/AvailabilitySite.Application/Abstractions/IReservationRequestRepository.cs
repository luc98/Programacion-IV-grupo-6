using AvailabilitySite.Application.Reservations;

namespace AvailabilitySite.Application.Abstractions;

public interface IReservationRequestRepository
{
    Task<ReservationRequestRecord?> FindByIdempotencyKeyAsync(
        string userId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        ReservationRequestRecord request,
        CancellationToken cancellationToken = default);

    Task<(ReservationRequestRecord Request, bool Created)> AddOrGetExistingAsync(
        ReservationRequestRecord request,
        CancellationToken cancellationToken = default);
}
