namespace AvailabilitySite.Domain.Entities;

// Entidad anémica: representa únicamente datos propios de Availability.
public sealed class Event
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;
}
