using AvailabilitySite.Application.Availability;
using AvailabilitySite.Application.Common;
using AvailabilitySite.Web.Models;
using AvailabilitySite.Web.Presentation;
using Microsoft.AspNetCore.Mvc;

namespace AvailabilitySite.Web.Controllers;

// Consulta pública de disponibilidad: no requiere iniciar sesión.
public class EventsController(IGetEventAvailabilityUseCase getAvailability, EventLookup lookup) : AppController
{
    // id: evento. seat: asiento elegido antes (por ejemplo, al volver de la confirmación).
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Availability(Guid? id, Guid? seat, CancellationToken cancellationToken)
    {
        // EventAvailabilityDto no trae nombre, lugar ni fecha: se toman de la lista de eventos.
        // Así también se detecta un evento inexistente (el caso de uso devuelve una lista vacía).
        var ev = id is { } eventId ? await lookup.FindEventAsync(eventId, cancellationToken) : null;
        if (ev is null)
            return StatusPage(StatusCodes.Status404NotFound, "Evento inexistente", "No encontramos el evento que buscás.");

        var result = await getAvailability.ExecuteAsync(ev.EventId, cancellationToken);
        if (!result.IsSuccess)
        {
            return result.Error!.Type == ApplicationErrorType.Validation
                ? StatusPage(StatusCodes.Status404NotFound, "Evento inexistente", "No encontramos el evento que buscás.")
                : ErrorPage(result.Error, retryUrl: Url.Action(nameof(Availability), new { id, seat }));
        }

        var seats = result.Value!.Seats;
        Guid? preselected = null;
        string? unavailableLabel = null;
        if (seat is not null && seats.FirstOrDefault(s => s.SeatId == seat) is { } previous)
        {
            // La disponibilidad se vuelve a comprobar siempre: lo que se vio libre puede ya no estarlo.
            if (previous.IsAvailable)
                preselected = previous.SeatId;
            else
                unavailableLabel = previous.Label;
        }

        return View(new AvailabilityViewModel
        {
            Event = ev,
            Seats = seats,
            RetrievedAt = DateTimeOffset.UtcNow,
            PreselectedSeat = preselected,
            PreselectedSeatUnavailableLabel = unavailableLabel,
        });
    }
}
