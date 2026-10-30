using AvailabilitySite.Application.Common;
using AvailabilitySite.Application.Reservations;

namespace AvailabilitySite.Application.Abstractions;

public interface IReservationStatusClient
{
    Task<Result<ReservationTrackingDto>> GetAsync(Guid requestId, CancellationToken cancellationToken = default);
}
