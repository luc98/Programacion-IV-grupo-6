using AvailabilitySite.Application.Abstractions;
using AvailabilitySite.Domain.Entities;

namespace AvailabilitySite.Infrastructure.Availability;

// Implementación temporal para desarrollo local. No representa persistencia real.
public sealed class InMemoryAvailabilityQueryRepository : IAvailabilityQueryRepository
{
    private static readonly IReadOnlyList<Event> Events =
    [
        new Event
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Name = "Conferencia de prueba"
        },
        new Event
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Name = "Recital de prueba"
        }
    ];

    private static readonly IReadOnlyList<Seat> Seats =
    [
        new Seat
        {
            Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1"),
            EventId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Label = "A-01",
            IsAvailable = true
        },
        new Seat
        {
            Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2"),
            EventId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Label = "A-02",
            IsAvailable = false
        },
        new Seat
        {
            Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1"),
            EventId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Label = "B-01",
            IsAvailable = true
        }
    ];

    public Task<IReadOnlyList<Seat>> GetSeatsAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Events.All(@event => @event.Id != eventId))
        {
            return Task.FromResult<IReadOnlyList<Seat>>([]);
        }

        IReadOnlyList<Seat> result = Seats
            .Where(seat => seat.EventId == eventId)
            .ToList();

        return Task.FromResult(result);
    }
}
