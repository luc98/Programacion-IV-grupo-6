using AvailabilitySite.Application.Abstractions;
using AvailabilitySite.Application.Events;
using AvailabilitySite.Domain.Entities;
using AvailabilitySite.Infrastructure.Persistence.MongoDb.Documents;
using MongoDB.Driver;

namespace AvailabilitySite.Infrastructure.Persistence.MongoDb;

public sealed class MongoAvailabilityQueryRepository(MongoDbContext context) : IAvailabilityQueryRepository
{
    // Contrato actual: un evento inexistente devuelve una lista vacía, igual que un evento sin asientos.
    // Para distinguirlos están FindSeatsAsync y EventExistsAsync.
    public async Task<IReadOnlyList<Seat>> GetSeatsAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        await FindSeatsAsync(eventId, cancellationToken) ?? [];

    // null si el evento no existe; lista vacía si existe y no tiene asientos.
    public Task<IReadOnlyList<Seat>?> FindSeatsAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        MongoErrors.ExecuteAsync<IReadOnlyList<Seat>?>(async () =>
        {
            if (!await ExistsAsync(eventId, cancellationToken))
            {
                return null;
            }

            var seats = await context.Seats
                .Find(seat => seat.EventId == eventId)
                .SortBy(seat => seat.Label)
                .ToListAsync(cancellationToken);

            return seats.Select(ToEntity).ToList();
        });

    public Task<bool> EventExistsAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        MongoErrors.ExecuteAsync(() => ExistsAsync(eventId, cancellationToken));

    // Para cuando se implemente IListEventsUseCase con datos reales.
    public Task<IReadOnlyList<EventSummaryDto>> ListEventsAsync(CancellationToken cancellationToken = default) =>
        MongoErrors.ExecuteAsync<IReadOnlyList<EventSummaryDto>>(async () =>
        {
            var events = await context.Events
                .Find(FilterDefinition<EventDocument>.Empty)
                .SortBy(e => e.StartsAt)
                .ToListAsync(cancellationToken);

            return events.Select(ToSummary).ToList();
        });

    public Task<EventSummaryDto?> FindEventAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        MongoErrors.ExecuteAsync(async () =>
        {
            var document = await context.Events.Find(e => e.EventId == eventId).FirstOrDefaultAsync(cancellationToken);
            return document is null ? null : ToSummary(document);
        });

    private async Task<bool> ExistsAsync(Guid eventId, CancellationToken cancellationToken) =>
        await context.Events.Find(e => e.EventId == eventId).Limit(1).AnyAsync(cancellationToken);

    private static Seat ToEntity(SeatDocument document) => new()
    {
        Id = document.SeatId,
        EventId = document.EventId,
        Label = document.Label,
        IsAvailable = document.IsAvailable,
    };

    private static EventSummaryDto ToSummary(EventDocument document) =>
        new(document.EventId, document.Name, document.Venue, new DateTimeOffset(document.StartsAt, TimeSpan.Zero));
}

