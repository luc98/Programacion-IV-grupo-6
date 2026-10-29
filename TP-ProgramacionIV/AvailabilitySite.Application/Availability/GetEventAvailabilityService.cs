using AvailabilitySite.Application.Abstractions;
using AvailabilitySite.Application.Common;

namespace AvailabilitySite.Application.Availability;

public sealed class GetEventAvailabilityService(
    IAvailabilityQueryRepository availabilityQueryRepository)
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

        var result = seats
            .Select(seat => new SeatAvailabilityDto(seat.Id, seat.Label, seat.IsAvailable))
            .ToList();

        return Result<EventAvailabilityDto>.Success(
            new EventAvailabilityDto(eventId, result));
    }
}
