using AvailabilitySite.Application.Abstractions;
using AvailabilitySite.Application.Common;

namespace AvailabilitySite.Application.Availability;

public sealed class GetEventAvailabilityService(
    IAvailabilityQueryRepository availabilityQueryRepository,
    IOccupiedSeatsClient occupiedSeatsClient)
    : IGetEventAvailabilityUseCase
{
    public async Task<Result<EventAvailabilityDto>> ExecuteAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        if (eventId == Guid.Empty)
        {
            return Result<EventAvailabilityDto>.Failure(
                new ApplicationError(
                    ApplicationErrorType.Validation,
                    "event_id_required",
                    "El identificador del evento es obligatorio."));
        }

        var seats = await availabilityQueryRepository
            .GetSeatsAsync(eventId, cancellationToken);

        // Mongo guarda el catálogo y Booking las reservas. Si Booking no responde se muestra el catálogo:
        // la vista es eventualmente consistente (3.g) y Booking rechaza igual un asiento ocupado.
        var occupied = await occupiedSeatsClient.GetOccupiedSeatIdsAsync(eventId, cancellationToken);

        var result = seats
            .Select(seat => new SeatAvailabilityDto(
                seat.Id,
                seat.Label,
                seat.IsAvailable && occupied?.Contains(seat.Id) != true))
            .ToList();

        return Result<EventAvailabilityDto>.Success(
            new EventAvailabilityDto(eventId, result));
    }
}
