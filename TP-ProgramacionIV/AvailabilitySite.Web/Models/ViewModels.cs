using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using AvailabilitySite.Application.Availability;
using AvailabilitySite.Application.Events;
using AvailabilitySite.Application.Reservations;
using AvailabilitySite.Web.Presentation;

namespace AvailabilitySite.Web.Models;

// Modelos de pantalla. Envuelven los DTOs de Application sin cambiarlos.

public sealed record PageLink(string Text, string Url);

// Pantalla genérica para errores (404, 403, 503...). Nunca incluye detalles técnicos.
public sealed class StatusPageViewModel
{
    public required int StatusCode { get; init; }
    public required string Title { get; init; }
    public required string Message { get; init; }
    public string? RetryUrl { get; init; }
    public IReadOnlyList<PageLink> Links { get; init; } = [];
}

public sealed class EventListViewModel
{
    public IReadOnlyList<EventSummaryDto> Events { get; init; } = [];
    public string? ErrorMessage { get; init; }
}

// Asiento listo para dibujar en el mapa: la fila y el número se sacan de la etiqueta ("A-01" → fila A, 1).
public sealed partial record SeatCell(SeatAvailabilityDto Seat, string Row, string Number, int Order)
{
    public static SeatCell From(SeatAvailabilityDto seat)
    {
        var match = LabelPattern().Match(seat.Label);
        return match.Success
            ? new(seat, match.Groups[1].Value.ToUpperInvariant(), int.Parse(match.Groups[2].Value).ToString(), int.Parse(match.Groups[2].Value))
            : new(seat, "", seat.Label, int.MaxValue);
    }

    [GeneratedRegex(@"^\s*([A-Za-z]+)[\s-]*0*(\d+)\s*$")]
    private static partial Regex LabelPattern();
}

public sealed class AvailabilityViewModel
{
    public required EventSummaryDto Event { get; init; }
    public required IReadOnlyList<SeatAvailabilityDto> Seats { get; init; }
    public required DateTimeOffset RetrievedAt { get; init; }

    // Asiento elegido antes (por ejemplo, al volver de la confirmación) que sigue disponible.
    public Guid? PreselectedSeat { get; init; }

    // El asiento elegido antes ya no está disponible.
    public string? PreselectedSeatUnavailableLabel { get; init; }

    public int AvailableCount => Seats.Count(s => s.IsAvailable);
    public int OccupiedCount => Seats.Count(s => !s.IsAvailable);

    public IReadOnlyList<IGrouping<string, SeatCell>> Rows =>
        Seats.Select(SeatCell.From)
            .OrderBy(c => c.Order).ThenBy(c => c.Seat.Label, StringComparer.Ordinal)
            .GroupBy(c => c.Row)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToList();
}

// Datos enviados por el formulario de confirmación. Se validan en el servidor.
public sealed class ConfirmReservationInput
{
    [Required(ErrorMessage = "Falta el evento.")]
    public Guid? EventId { get; set; }

    [Required(ErrorMessage = "Elegí un asiento.")]
    public Guid? SeatId { get; set; }

    // Se genera al mostrar el formulario y se conserva en los reintentos de la misma operación.
    [Required(ErrorMessage = "Falta la clave de la solicitud. Volvé a cargar la página.")]
    public string? IdempotencyKey { get; set; }
}

public enum SubmissionProblem
{
    None,

    // Se sabe que la solicitud no se registró: se puede reintentar.
    NotRegistered,

    // No se sabe si la solicitud se registró: reintentar solo con la misma clave.
    OutcomeUnknown,
}

public sealed class ConfirmReservationViewModel
{
    public required ConfirmReservationInput Input { get; init; }
    public EventSummaryDto? Event { get; init; }
    public SeatAvailabilityDto? Seat { get; init; }
    public DateTimeOffset? CheckedAt { get; init; }
    public SubmissionProblem Problem { get; init; }
    public string? ProblemMessage { get; init; }

    public bool SeatAvailable => Seat?.IsAvailable == true;

    // Con resultado desconocido se permite reenviar aunque el asiento ya figure ocupado:
    // puede estar ocupado justamente por esta misma solicitud.
    public bool CanSubmit => Event is not null && Seat is not null && (SeatAvailable || Problem == SubmissionProblem.OutcomeUnknown);
}

// ReservationTrackingDto + nombre del evento y etiqueta del asiento (que el DTO no trae).
public sealed class TrackingView
{
    public required ReservationTrackingDto Tracking { get; init; }
    public EventSummaryDto? Event { get; init; }
    public required string SeatLabel { get; init; }
    public required DateTimeOffset RetrievedAt { get; init; }

    public TrackingState State => StatusTexts.Interpret(Tracking.RequestStatus, Tracking.BookingStatus);
    public bool IsTerminal => StatusTexts.IsTerminal(State);
}

public sealed class TrackingPageViewModel
{
    public required Guid RequestId { get; init; }
    public TrackingView? Tracking { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed record MyRequestRow(ReservationRequestSummaryDto Request, string EventName, string SeatLabel)
{
    public TrackingState State => StatusTexts.Interpret(Request.RequestStatus, Request.BookingStatus);
}

public sealed class MyRequestsViewModel
{
    public IReadOnlyList<MyRequestRow> Requests { get; init; } = [];
    public string? ErrorMessage { get; init; }
}
