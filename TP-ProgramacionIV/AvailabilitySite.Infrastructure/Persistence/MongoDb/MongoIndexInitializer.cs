using AvailabilitySite.Infrastructure.Persistence.MongoDb.Documents;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;

namespace AvailabilitySite.Infrastructure.Persistence.MongoDb;

// Crea los índices y siembra datos iniciales al arrancar la aplicación si las colecciones están vacías.
// Es idempotente: si ya existen índices o datos, no hace nada.
public sealed class MongoIndexInitializer(MongoDbContext context)
{
    public const string SeatsByEvent = "ux_seats_event_seat";
    public const string RequestsByIdempotencyKey = "ux_requests_user_idempotency_key";
    public const string RequestsByUserAndDate = "ix_requests_user_created_at";
    public const string OutboxByStatusAndDate = "ix_outbox_status_created_at";
    public const string OutboxByStatusAndNextAttempt = "ix_outbox_status_next_attempt";
    public const string OutboxByRequest = "ux_outbox_request_message_type";

    public Task EnsureIndexesAsync(CancellationToken cancellationToken = default) =>
        MongoErrors.ExecuteAsync(async () =>
        {
            await context.Seats.Indexes.CreateOneAsync(
                new CreateIndexModel<SeatDocument>(
                    Builders<SeatDocument>.IndexKeys.Ascending(s => s.EventId).Ascending(s => s.SeatId),
                    new CreateIndexOptions { Name = SeatsByEvent, Unique = true }),
                cancellationToken: cancellationToken);

            await context.ReservationRequests.Indexes.CreateManyAsync(
                [
                    new CreateIndexModel<ReservationRequestDocument>(
                        Builders<ReservationRequestDocument>.IndexKeys.Ascending(r => r.UserId).Ascending(r => r.IdempotencyKey),
                        new CreateIndexOptions { Name = RequestsByIdempotencyKey, Unique = true }),
                    new CreateIndexModel<ReservationRequestDocument>(
                        Builders<ReservationRequestDocument>.IndexKeys.Ascending(r => r.UserId).Descending(r => r.CreatedAt),
                        new CreateIndexOptions { Name = RequestsByUserAndDate }),
                ],
                cancellationToken);

            await context.OutboxMessages.Indexes.CreateManyAsync(
                [
                    new CreateIndexModel<OutboxMessageDocument>(
                        Builders<OutboxMessageDocument>.IndexKeys.Ascending(m => m.Status).Ascending(m => m.CreatedAt),
                        new CreateIndexOptions { Name = OutboxByStatusAndDate }),
                    new CreateIndexModel<OutboxMessageDocument>(
                        Builders<OutboxMessageDocument>.IndexKeys.Ascending(m => m.Status).Ascending(m => m.NextAttemptAt),
                        new CreateIndexOptions { Name = OutboxByStatusAndNextAttempt }),
                    new CreateIndexModel<OutboxMessageDocument>(
                        Builders<OutboxMessageDocument>.IndexKeys.Ascending(m => m.RequestId).Ascending(m => m.MessageType),
                        new CreateIndexOptions { Name = OutboxByRequest, Unique = true }),
                ],
                cancellationToken);

            await SeedDatosInicialesAsync(cancellationToken);
        });

    private async Task SeedDatosInicialesAsync(CancellationToken cancellationToken)
    {
        var evento1Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var evento2Id = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var countEventos = await context.Events.CountDocumentsAsync(_ => true, cancellationToken: cancellationToken);
        if (countEventos == 0)
        {
            var eventos = new[]
            {
                new EventDocument
                {
                    EventId = evento1Id,
                    Name = "Conferencia de prueba",
                    Venue = "Auditorio Central",
                    StartsAt = new DateTime(2026, 12, 10, 22, 0, 0, DateTimeKind.Utc)
                },
                new EventDocument
                {
                    EventId = evento2Id,
                    Name = "Recital de prueba",
                    Venue = "Estadio Principal",
                    StartsAt = new DateTime(2026, 12, 18, 23, 30, 0, DateTimeKind.Utc)
                }
            };

            await context.Events.InsertManyAsync(eventos, cancellationToken: cancellationToken);
        }

        var countSeats = await context.Seats.CountDocumentsAsync(_ => true, cancellationToken: cancellationToken);
        if (countSeats == 0)
        {
            var asientos = new[]
            {
                new SeatDocument { SeatId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1"), EventId = evento1Id, Label = "A-01", IsAvailable = true },
                new SeatDocument { SeatId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2"), EventId = evento1Id, Label = "A-02", IsAvailable = true },
                new SeatDocument { SeatId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3"), EventId = evento1Id, Label = "A-03", IsAvailable = true },
                new SeatDocument { SeatId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa4"), EventId = evento1Id, Label = "A-04", IsAvailable = true },
                new SeatDocument { SeatId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1"), EventId = evento2Id, Label = "B-01", IsAvailable = true },
                new SeatDocument { SeatId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb2"), EventId = evento2Id, Label = "B-02", IsAvailable = true }
            };

            await context.Seats.InsertManyAsync(asientos, cancellationToken: cancellationToken);
        }
    }
}

internal sealed class MongoIndexInitializerHostedService(MongoIndexInitializer initializer) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => initializer.EnsureIndexesAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
