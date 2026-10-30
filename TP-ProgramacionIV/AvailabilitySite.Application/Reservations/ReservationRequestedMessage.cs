namespace AvailabilitySite.Application.Reservations;

// Contrato JSON compatible con Booking.SolicitudReservaDto.
public sealed record ReservationRequestedMessage(
    Guid SolicitudId,
    Guid EventoId,
    Guid AsientoId,
    string UsuarioId);
