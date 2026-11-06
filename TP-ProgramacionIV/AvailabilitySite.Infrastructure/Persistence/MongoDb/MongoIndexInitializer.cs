using AvailabilitySite.Infrastructure.Persistence.MongoDb.Documents;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;

namespace AvailabilitySite.Infrastructure.Persistence.MongoDb;

// Crea los índices de MongoDB al arrancar la aplicación.
// El seeding de datos de prueba está separado y se registra únicamente en Development.
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

        });
}

internal sealed class MongoIndexInitializerHostedService(MongoIndexInitializer initializer) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => initializer.EnsureIndexesAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
