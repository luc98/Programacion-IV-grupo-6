using AvailabilitySite.Application.Availability;
using AvailabilitySite.Application.Events;

namespace AvailabilitySite.Web.Presentation;

// Los DTOs de disponibilidad y seguimiento traen solo ids (EventId, SeatId). Para mostrar nombre del
// evento y etiqueta del asiento, el front los busca con los casos de uso de eventos y disponibilidad.
// Se guarda en memoria durante el request para no repetir consultas (por ejemplo en "Mis solicitudes").
public sealed class EventLookup(IListEventsUseCase listEvents, IGetEventAvailabilityUseCase getAvailability)
{
    private IReadOnlyList<EventSummaryDto>? _events;
    private readonly Dictionary<Guid, IReadOnlyList<SeatAvailabilityDto>?> _seats = [];

    // null si el evento no existe o si no se pudo consultar.
    public async Task<EventSummaryDto?> FindEventAsync(Guid eventId, CancellationToken cancellationToken)
    {
        if (_events is null)
        {
            var result = await listEvents.ExecuteAsync(cancellationToken);
            if (!result.IsSuccess)
                return null;
            _events = result.Value!;
        }

        return _events.FirstOrDefault(e => e.EventId == eventId);
    }

    // Etiqueta del asiento ("A-01"); si no se puede obtener, un id abreviado.
    public async Task<string> SeatLabelAsync(Guid eventId, Guid seatId, CancellationToken cancellationToken)
    {
        if (!_seats.TryGetValue(eventId, out var seats))
        {
            var result = await getAvailability.ExecuteAsync(eventId, cancellationToken);
            seats = result.IsSuccess ? result.Value!.Seats : null;
            _seats[eventId] = seats;
        }

        return seats?.FirstOrDefault(s => s.SeatId == seatId)?.Label ?? seatId.ToString()[..8];
    }
}
