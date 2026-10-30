using AvailabilitySite.Application.Reservations;

namespace AvailabilitySite.Application.Abstractions;

public interface IReservationRequestPublisher
{
    Task PublishAsync(
        ReservationRequestedMessage message,
        CancellationToken cancellationToken = default);
}
