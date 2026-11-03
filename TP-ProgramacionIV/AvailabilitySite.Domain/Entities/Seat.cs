namespace AvailabilitySite.Domain.Entities;

// La reserva definitiva y la concurrencia pertenecen a Booking, no a esta entidad.
public sealed class Seat
{
    public Guid Id { get; init; }

    public Guid EventId { get; init; }

    public string Label { get; init; } = string.Empty;

    public bool IsAvailable { get; init; }
}
