using System.Text.Json;
using AvailabilitySite.Application.Abstractions;
using AvailabilitySite.Application.Reservations;
using AvailabilitySite.Infrastructure.Outbox;
using AvailabilitySite.Infrastructure.Persistence.MongoDb.Documents;
using MongoDB.Driver;

namespace AvailabilitySite.Infrastructure.Persistence.MongoDb;

public sealed class MongoReservationRequestRepository(MongoDbContext context) : IReservationRequestRepository
{
    public const string ReservationRequestedMessageType = "ReservationRequested";

    private static readonly TransactionOptions TransactionOptions = new(
        readConcern: ReadConcern.Snapshot,
        writeConcern: WriteConcern.WMajority);

    public Task<ReservationRequestRecord?> FindByIdempotencyKeyAsync(
        string userId,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        MongoErrors.ExecuteAsync(() => FindByKeyAsync(userId, idempotencyKey, cancellationToken));

    // Guarda la solicitud y su mensaje de Outbox en una sola transacción: quedan los dos o ninguno.
    // Si la clave de idempotencia ya existe (por ejemplo, dos envíos simultáneos), lanza
    // DuplicateReservationRequestException con la solicitud original.
    public Task AddAsync(ReservationRequestRecord request, CancellationToken cancellationToken = default) =>
        MongoErrors.ExecuteAsync(async () =>
        {
            try
            {
                using var session = await context.Client.StartSessionAsync(cancellationToken: cancellationToken);
                await session.WithTransactionAsync(
                    async (transaction, token) =>
                    {
                        await context.ReservationRequests.InsertOneAsync(transaction, ToDocument(request), cancellationToken: token);
                        await context.OutboxMessages.InsertOneAsync(transaction, ToOutboxDocument(request), cancellationToken: token);
                        return true;
                    },
                    TransactionOptions,
                    cancellationToken);
            }
            catch (Exception exception) when (MongoErrors.IsDuplicateKey(exception))
            {
                var existing = await FindByKeyAsync(request.UserId, request.IdempotencyKey, cancellationToken);
                if (existing is null)
                {
                    // El duplicado no es de la clave de idempotencia (por ejemplo, RequestId repetido).
                    throw new AvailabilityPersistenceException(
                        "No se pudo registrar la solicitud porque ya existe un registro con los mismos datos.",
                        isTransient: false,
                        exception);
                }

                throw new DuplicateReservationRequestException(existing);
            }
        });

    // Variante sin excepción para la idempotencia concurrente: devuelve la solicitud guardada
    // (la nueva o la original) e indica si se creó. Propuesta para el contrato de Application.
    public async Task<(ReservationRequestRecord Request, bool Created)> AddOrGetExistingAsync(
        ReservationRequestRecord request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await AddAsync(request, cancellationToken);
            return (request, true);
        }
        catch (DuplicateReservationRequestException duplicate)
        {
            return (duplicate.Existing, false);
        }
    }

    public Task<ReservationRequestRecord?> FindByIdAsync(Guid requestId, CancellationToken cancellationToken = default) =>
        MongoErrors.ExecuteAsync(async () =>
        {
            var document = await context.ReservationRequests
                .Find(r => r.RequestId == requestId)
                .FirstOrDefaultAsync(cancellationToken);
            return document is null ? null : ToRecord(document);
        });

    // Solicitudes del usuario, de la más reciente a la más antigua.
    public Task<IReadOnlyList<ReservationRequestRecord>> ListByUserAsync(
        string userId,
        int limit = 100,
        CancellationToken cancellationToken = default) =>
        MongoErrors.ExecuteAsync<IReadOnlyList<ReservationRequestRecord>>(async () =>
        {
            var documents = await context.ReservationRequests
                .Find(r => r.UserId == userId)
                .SortByDescending(r => r.CreatedAt)
                .Limit(limit)
                .ToListAsync(cancellationToken);
            return documents.Select(ToRecord).ToList();
        });

    private async Task<ReservationRequestRecord?> FindByKeyAsync(string userId, string idempotencyKey, CancellationToken cancellationToken)
    {
        var document = await context.ReservationRequests
            .Find(r => r.UserId == userId && r.IdempotencyKey == idempotencyKey)
            .FirstOrDefaultAsync(cancellationToken);
        return document is null ? null : ToRecord(document);
    }

    private static ReservationRequestDocument ToDocument(ReservationRequestRecord record) => new()
    {
        RequestId = record.RequestId,
        EventId = record.EventId,
        SeatId = record.SeatId,
        UserId = record.UserId,
        IdempotencyKey = record.IdempotencyKey,
        RequestStatus = record.Status,
        CreatedAt = record.ReceivedAt.UtcDateTime,
    };

    private static ReservationRequestRecord ToRecord(ReservationRequestDocument document) => new(
        document.RequestId,
        document.EventId,
        document.SeatId,
        document.UserId,
        document.IdempotencyKey,
        new DateTimeOffset(document.CreatedAt, TimeSpan.Zero),
        document.RequestStatus);

    // El payload respeta el contrato acordado con Booking (SolicitudId, EventoId, AsientoId, UsuarioId).
    private static OutboxMessageDocument ToOutboxDocument(ReservationRequestRecord record)
    {
        var message = new ReservationRequestedMessage(record.RequestId, record.EventId, record.SeatId, record.UserId);
        var now = record.ReceivedAt.UtcDateTime;
        return new OutboxMessageDocument
        {
            MessageId = Guid.NewGuid(),
            RequestId = record.RequestId,
            MessageType = ReservationRequestedMessageType,
            Payload = JsonSerializer.Serialize(message),
            CreatedAt = now,
            Status = OutboxMessageStatus.Pending,
            RetryCount = 0,
            NextAttemptAt = now,
        };
    }
}
