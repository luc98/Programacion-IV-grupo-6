using AvailabilitySite.Application.Availability;
using AvailabilitySite.Application.Common;
using AvailabilitySite.Application.Reservations;
using AvailabilitySite.Web.Models;
using AvailabilitySite.Web.Presentation;
using Microsoft.AspNetCore.Mvc;

namespace AvailabilitySite.Web.Controllers;

// Envío y seguimiento de solicitudes. El controller no decide reglas de negocio:
// delega en los casos de uso de Application y solo traduce resultados a pantallas.
public class ReservationsController(
    IGetEventAvailabilityUseCase getAvailability,
    IRequestReservationUseCase requestReservation,
    IGetReservationRequestStatusUseCase getStatus,
    IListCurrentUserReservationRequestsUseCase listMine,
    EventLookup lookup) : AppController
{
    // ------------------------------------------------------------------ confirmación (GET)

    [HttpGet]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Confirm(Guid? eventId, Guid? seatId, CancellationToken cancellationToken)
    {
        if (eventId is null || eventId == Guid.Empty)
            return StatusPage(StatusCodes.Status404NotFound, "Evento inexistente", "No encontramos el evento que buscás.");

        if (seatId is null || seatId == Guid.Empty)
        {
            TempData[Flash.Warning] = "Elegí un asiento disponible para continuar.";
            return RedirectToAction("Availability", "Events", new { id = eventId });
        }

        // Nueva operación lógica → nueva clave de idempotencia.
        var input = new ConfirmReservationInput { EventId = eventId, SeatId = seatId, IdempotencyKey = Guid.NewGuid().ToString() };
        return await ConfirmView(input, SubmissionProblem.None, null, cancellationToken);
    }

    // ------------------------------------------------------------------ envío (POST → Redirect → GET)

    [HttpPost]
    public async Task<IActionResult> Confirm([FromForm] ConfirmReservationInput input, CancellationToken cancellationToken)
    {
        if (input.EventId is null || input.EventId == Guid.Empty)
            return StatusPage(StatusCodes.Status404NotFound, "Evento inexistente", "No encontramos el evento que buscás.");

        if (!ModelState.IsValid)
            return await ConfirmView(input, SubmissionProblem.None, null, cancellationToken);

        var result = await requestReservation.ExecuteAsync(
            new CreateReservationRequestDto(input.EventId.Value, input.SeatId!.Value, input.IdempotencyKey!), cancellationToken);

        if (result.IsSuccess)
        {
            TempData[Flash.Success] = result.Value!.IsReplay
                ? "Ya habíamos recibido esta solicitud; no se envió de nuevo. Este es su seguimiento."
                : "Recibimos tu solicitud. Eso todavía no significa que la reserva esté confirmada: seguí su estado acá.";
            return RedirectToAction(nameof(Track), new { id = result.Value.RequestId });
        }

        var error = result.Error!;
        switch (error.Type)
        {
            case ApplicationErrorType.Unauthenticated:
                return ErrorPage(error);

            case ApplicationErrorType.Validation:
                ModelState.AddModelError(string.Empty, error.Message);
                return await ConfirmView(input, SubmissionProblem.None, null, cancellationToken);

            case ApplicationErrorType.NotFound:
                return StatusPage(StatusCodes.Status404NotFound, "No encontrado", error.Message, links:
                    new PageLink("Volver a la disponibilidad", Url.Action("Availability", "Events", new { id = input.EventId })!));

            case ApplicationErrorType.OutcomeUnknown:
                // No se reenvía automáticamente. Se conserva la MISMA clave para que un reintento manual
                // recupere la solicitud original si ya había sido aceptada.
                return await ConfirmView(input, SubmissionProblem.OutcomeUnknown, error.Message, cancellationToken);

            default:
                return await ConfirmView(input, SubmissionProblem.NotRegistered, error.Message, cancellationToken);
        }
    }

    // ------------------------------------------------------------------ seguimiento

    [HttpGet]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Track(Guid? id, CancellationToken cancellationToken)
    {
        if (id is not { } requestId || requestId == Guid.Empty)
            return StatusPage(StatusCodes.Status404NotFound, "Solicitud inexistente", "No encontramos esa solicitud.");

        var result = await getStatus.ExecuteAsync(requestId, cancellationToken);
        if (result.IsSuccess)
            return View(new TrackingPageViewModel { RequestId = requestId, Tracking = await TrackingViewAsync(result.Value!, cancellationToken) });

        var error = result.Error!;
        return error.Type switch
        {
            ApplicationErrorType.NotFound => StatusPage(StatusCodes.Status404NotFound, "Solicitud inexistente", error.Message,
                links: [new PageLink("Ver mis solicitudes", Url.Action(nameof(Index))!), new PageLink("Ir a eventos", Url.Action("Index", "Home")!)]),
            ApplicationErrorType.Forbidden => StatusPage(StatusCodes.Status403Forbidden, "Acceso no autorizado",
                "Esta solicitud no pertenece a tu cuenta.", links: new PageLink("Ver mis solicitudes", Url.Action(nameof(Index))!)),
            ApplicationErrorType.DependencyUnavailable =>
                // Error temporal: se muestra la pantalla de seguimiento igual, para que el polling se recupere solo.
                View(new TrackingPageViewModel { RequestId = requestId, ErrorMessage = error.Message }),
            _ => ErrorPage(error),
        };
    }

    // Endpoint de lectura para el polling. Devuelve el fragmento HTML del estado (lo renderiza el servidor,
    // así los textos y reglas no se duplican en JavaScript). Usa el mismo caso de uso que la pantalla.
    [HttpGet]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> TrackStatus(Guid? id, CancellationToken cancellationToken)
    {
        if (id is not { } requestId || requestId == Guid.Empty)
            return PlainStatus(StatusCodes.Status404NotFound, "Solicitud inexistente.");

        var result = await getStatus.ExecuteAsync(requestId, cancellationToken);
        if (result.IsSuccess)
            return PartialView("_TrackingPanel", await TrackingViewAsync(result.Value!, cancellationToken));

        return result.Error!.Type switch
        {
            ApplicationErrorType.Unauthenticated => PlainStatus(StatusCodes.Status401Unauthorized, "Necesitás iniciar sesión."),
            ApplicationErrorType.NotFound => PlainStatus(StatusCodes.Status404NotFound, "Solicitud inexistente."),
            ApplicationErrorType.Forbidden => PlainStatus(StatusCodes.Status403Forbidden, "Acceso no autorizado."),
            _ => PlainStatus(StatusCodes.Status503ServiceUnavailable, "Servicio no disponible."),
        };
    }

    // ------------------------------------------------------------------ mis solicitudes

    [HttpGet]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var result = await listMine.ExecuteAsync(cancellationToken);
        if (!result.IsSuccess && result.Error!.Type == ApplicationErrorType.Unauthenticated)
            return ErrorPage(result.Error);

        if (!result.IsSuccess)
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;

        var rows = new List<MyRequestRow>();
        foreach (var request in result.IsSuccess ? result.Value! : [])
        {
            var ev = await lookup.FindEventAsync(request.EventId, cancellationToken);
            var seatLabel = await lookup.SeatLabelAsync(request.EventId, request.SeatId, cancellationToken);
            rows.Add(new MyRequestRow(request, ev?.Name ?? "Evento", seatLabel));
        }

        return View(new MyRequestsViewModel { Requests = rows, ErrorMessage = result.Error?.Message });
    }

    // ------------------------------------------------------------------ helpers

    private async Task<TrackingView> TrackingViewAsync(ReservationTrackingDto tracking, CancellationToken cancellationToken) => new()
    {
        Tracking = tracking,
        Event = await lookup.FindEventAsync(tracking.EventId, cancellationToken),
        SeatLabel = await lookup.SeatLabelAsync(tracking.EventId, tracking.SeatId, cancellationToken),
        RetrievedAt = DateTimeOffset.UtcNow,
    };

    private async Task<IActionResult> ConfirmView(
        ConfirmReservationInput input, SubmissionProblem problem, string? problemMessage, CancellationToken cancellationToken)
    {
        var eventId = input.EventId!.Value;
        var ev = await lookup.FindEventAsync(eventId, cancellationToken);
        if (ev is null && problem == SubmissionProblem.None)
            return StatusPage(StatusCodes.Status404NotFound, "Evento inexistente", "No encontramos el evento que buscás.");

        // Se vuelve a consultar la disponibilidad cada vez que se muestra la confirmación.
        var availability = await getAvailability.ExecuteAsync(eventId, cancellationToken);
        if (!availability.IsSuccess && problem == SubmissionProblem.None)
            return ErrorPage(availability.Error!, retryUrl: Url.Action(nameof(Confirm), new { eventId = input.EventId, seatId = input.SeatId }));

        var seat = availability.IsSuccess ? availability.Value!.Seats.FirstOrDefault(s => s.SeatId == input.SeatId) : null;
        if (availability.IsSuccess && seat is null && input.SeatId is not null)
        {
            return StatusPage(StatusCodes.Status404NotFound, "Asiento inexistente", "El asiento elegido no existe en este evento.",
                links: new PageLink("Volver a la disponibilidad", Url.Action("Availability", "Events", new { id = input.EventId })!));
        }

        return View("Confirm", new ConfirmReservationViewModel
        {
            Input = input,
            Event = ev,
            Seat = seat,
            CheckedAt = availability.IsSuccess ? DateTimeOffset.UtcNow : null,
            Problem = problem,
            ProblemMessage = problemMessage,
        });
    }

    private static ContentResult PlainStatus(int status, string message) =>
        new() { StatusCode = status, Content = message, ContentType = "text/plain; charset=utf-8" };
}
