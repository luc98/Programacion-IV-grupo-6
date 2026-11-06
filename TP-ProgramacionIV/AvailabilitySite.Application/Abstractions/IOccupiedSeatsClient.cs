namespace AvailabilitySite.Application.Abstractions;

// Booking es el dueño de las reservas: un asiento está ocupado mientras tenga una reserva retenida o
// confirmada. Devuelve null si Booking no responde, porque la consulta pública no debe caerse por eso.
public interface IOccupiedSeatsClient
{
    Task<IReadOnlySet<Guid>?> GetOccupiedSeatIdsAsync(Guid eventId, CancellationToken cancellationToken = default);
}
