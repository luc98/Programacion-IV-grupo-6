using AvailabilitySite.Application.Reservations;

namespace AvailabilitySite.Web.Presentation;

// Estado que muestra la pantalla de seguimiento. El contrato devuelve RequestStatus (enum, hoy solo
// Pending) y BookingStatus / RejectionReason como strings; acá se traducen a algo que la vista entiende.
// Booking informa los valores de sus enums EstadoReserva (Retenida, Confirmada, Rechazada, Vencida) y
// MotivoRechazo (Ninguno, AsientoOcupado, PagoFallido, PagoNoDisponible).
public enum TrackingState
{
    Pending,
    Held,
    Confirmed,
    Rejected,
    Expired,

    // Booking informó un valor que el front no conoce: se muestra tal cual y se sigue consultando.
    Unknown,
}

public sealed record StatusText(string Label, string Icon, string CssModifier, string Description);

public static class StatusTexts
{
    // Sin estado de Booking la solicitud todavía no fue procesada. Se aceptan también los nombres en inglés.
    public static TrackingState Interpret(ReservationRequestStatus requestStatus, string? bookingStatus) =>
        bookingStatus?.Trim().ToLowerInvariant() switch
        {
            null or "" => TrackingState.Pending,
            "retenida" or "held" => TrackingState.Held,
            "confirmada" or "confirmed" => TrackingState.Confirmed,
            "rechazada" or "rejected" => TrackingState.Rejected,
            "vencida" or "expired" => TrackingState.Expired,
            _ => TrackingState.Unknown,
        };

    // Booking manda "Ninguno" cuando no hay motivo de rechazo.
    public static string? RejectionReasonOrNull(string? reason) =>
        string.IsNullOrWhiteSpace(reason) || reason.Trim().Equals("ninguno", StringComparison.OrdinalIgnoreCase) ? null : reason;

    public static bool IsTerminal(TrackingState state) =>
        state is TrackingState.Confirmed or TrackingState.Rejected or TrackingState.Expired;

    public static StatusText ForSeat(bool isAvailable) => isAvailable
        ? new("Disponible", "○", "available", "Podés elegirlo.")
        : new("Ocupado", "✕", "confirmed", "No se puede elegir.");

    public static StatusText For(TrackingState state) => state switch
    {
        TrackingState.Pending => new("Solicitud pendiente", "…", "pending",
            "Recibimos tu solicitud y está en cola para ser procesada. Todavía no hay una reserva."),
        TrackingState.Held => new("Reserva retenida", "◐", "held",
            "El asiento está apartado para vos mientras se procesa el pago."),
        TrackingState.Confirmed => new("Reserva confirmada", "✓", "confirmed",
            "El pago fue exitoso y el asiento quedó reservado a tu nombre."),
        TrackingState.Rejected => new("Reserva rechazada", "✕", "rejected",
            "La reserva no se concretó."),
        TrackingState.Expired => new("Reserva vencida", "⌛", "expired",
            "La retención terminó sin un resultado de pago. El asiento quedó liberado."),
        TrackingState.Unknown => new("En proceso", "…", "pending",
            "Tu solicitud se está procesando."),
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    // Motivo de rechazo: los valores conocidos tienen un texto armado; cualquier otro se muestra tal cual.
    public static (string ShortLabel, string Description) ForRejection(string reason) => reason.Trim().ToLowerInvariant() switch
    {
        "asientoocupado" or "seatoccupied" => ("Asiento ocupado",
            "El asiento ya estaba ocupado cuando procesamos tu solicitud. Podés elegir otro asiento."),
        "pagofallido" or "paymentfailed" => ("Pago fallido",
            "El pago no fue aprobado. No se realizó la reserva y el asiento quedó liberado."),
        "pagonodisponible" or "paymentunavailable" => ("Pago no disponible",
            "El servicio de pagos no estaba disponible, por lo que no pudimos completar la reserva. El asiento quedó liberado."),
        _ => ("Motivo informado", reason),
    };

    public static bool IsPaymentRejection(string? reason) =>
        reason?.Trim().ToLowerInvariant() is "pagofallido" or "pagonodisponible" or "paymentfailed" or "paymentunavailable";
}
