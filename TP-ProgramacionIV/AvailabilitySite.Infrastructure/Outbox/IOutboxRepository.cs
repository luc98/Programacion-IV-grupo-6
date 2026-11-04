namespace AvailabilitySite.Infrastructure.Outbox;

// Operaciones del Outbox que usa el publisher (a cargo del backend principal).
// Flujo esperado del publisher:
//   1. ClaimBatchAsync / TryClaimNextAsync → reserva mensajes para este proceso.
//   2. Publicar en el broker.
//   3. Solo si el broker confirmó: MarkPublishedAsync. Si falló: MarkFailedAsync.
// Un mensaje puede publicarse dos veces (por ejemplo, si el proceso se cae entre 2 y 3),
// así que el consumidor tiene que ser idempotente por SolicitudId.
public interface IOutboxRepository
{
    // Guarda un mensaje pendiente fuera de una transacción. Las solicitudes nuevas ya guardan
    // su mensaje en la misma transacción (MongoReservationRequestRepository.AddAsync).
    Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default);

    // Solo lectura: mensajes pendientes listos para enviar, del más viejo al más nuevo.
    Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(int limit, CancellationToken cancellationToken = default);

    // Reclama atómicamente el próximo mensaje disponible: Pending con el reintento vencido,
    // o Processing con el lease vencido (proceso caído o reiniciado). Null si no hay.
    Task<OutboxMessage?> TryClaimNextAsync(string processorId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OutboxMessage>> ClaimBatchAsync(string processorId, int batchSize, CancellationToken cancellationToken = default);

    // Devuelve false si el mensaje ya no está reclamado por este proceso (por ejemplo, venció el lease).
    Task<bool> MarkPublishedAsync(Guid messageId, string processorId, CancellationToken cancellationToken = default);

    Task<bool> RenewLeaseAsync(Guid messageId, string processorId, TimeSpan leaseDuration, CancellationToken cancellationToken = default);

    // Registra el error, suma un intento y programa el reintento; al llegar a MaxAttempts queda en Failed.
    Task<OutboxFailureResult> MarkFailedAsync(Guid messageId, string processorId, string error, CancellationToken cancellationToken = default);

    // Recuperación manual: vuelve a Pending un mensaje Failed.
    Task<bool> RequeueAsync(Guid messageId, CancellationToken cancellationToken = default);

    Task<OutboxMessage?> FindByIdAsync(Guid messageId, CancellationToken cancellationToken = default);
}

public enum OutboxMessageStatus
{
    Pending,
    Processing,
    Published,
    Failed
}

public sealed record OutboxMessage(
    Guid MessageId,
    Guid RequestId,
    string MessageType,
    string Payload,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt,
    OutboxMessageStatus Status,
    int RetryCount,
    string? LastError,
    DateTimeOffset NextAttemptAt,
    string? LockedBy,
    DateTimeOffset? LockedUntil);

public sealed record OutboxFailureResult(
    bool Updated,
    bool MovedToFailed,
    int RetryCount,
    DateTimeOffset? NextAttemptAt);
