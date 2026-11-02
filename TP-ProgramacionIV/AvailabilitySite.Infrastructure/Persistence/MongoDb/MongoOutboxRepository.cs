using AvailabilitySite.Infrastructure.Outbox;
using AvailabilitySite.Infrastructure.Persistence.MongoDb.Documents;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace AvailabilitySite.Infrastructure.Persistence.MongoDb;

// Solo persistencia del Outbox. Publicar en Azure Service Bus y el worker que recorre los
// pendientes quedan a cargo del backend principal (ver IOutboxRepository).
public sealed class MongoOutboxRepository(MongoDbContext context, IOptions<OutboxOptions> options, TimeProvider timeProvider)
    : IOutboxRepository
{
    private const int MaxErrorLength = 2000;

    private readonly OutboxOptions _options = options.Value;

    public Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default) =>
        MongoErrors.ExecuteAsync(() =>
            context.OutboxMessages.InsertOneAsync(ToDocument(message), cancellationToken: cancellationToken));

    public Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(int limit, CancellationToken cancellationToken = default) =>
        MongoErrors.ExecuteAsync<IReadOnlyList<OutboxMessage>>(async () =>
        {
            var now = UtcNow();
            var documents = await context.OutboxMessages
                .Find(m => m.Status == OutboxMessageStatus.Pending && m.NextAttemptAt <= now)
                .SortBy(m => m.CreatedAt)
                .Limit(limit)
                .ToListAsync(cancellationToken);
            return documents.Select(ToMessage).ToList();
        });

    public Task<OutboxMessage?> TryClaimNextAsync(string processorId, CancellationToken cancellationToken = default) =>
        MongoErrors.ExecuteAsync(async () =>
        {
            var now = UtcNow();
            var filter = Builders<OutboxMessageDocument>.Filter.Or(
                Builders<OutboxMessageDocument>.Filter.Where(m => m.Status == OutboxMessageStatus.Pending && m.NextAttemptAt <= now),
                // Reclamado por un proceso que no terminó (caída o reinicio) y cuyo lease ya venció.
                Builders<OutboxMessageDocument>.Filter.Where(m => m.Status == OutboxMessageStatus.Processing && m.LockedUntil < now));

            var update = Builders<OutboxMessageDocument>.Update
                .Set(m => m.Status, OutboxMessageStatus.Processing)
                .Set(m => m.LockedBy, processorId)
                .Set(m => m.LockedUntil, now + _options.LeaseDuration);

            // FindOneAndUpdate es atómico: dos procesos nunca reclaman el mismo mensaje a la vez.
            var document = await context.OutboxMessages.FindOneAndUpdateAsync(
                filter,
                update,
                new FindOneAndUpdateOptions<OutboxMessageDocument>
                {
                    Sort = Builders<OutboxMessageDocument>.Sort.Ascending(m => m.CreatedAt),
                    ReturnDocument = ReturnDocument.After,
                },
                cancellationToken);

            return document is null ? null : ToMessage(document);
        });

    public async Task<IReadOnlyList<OutboxMessage>> ClaimBatchAsync(
        string processorId,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var claimed = new List<OutboxMessage>(batchSize);
        while (claimed.Count < batchSize && await TryClaimNextAsync(processorId, cancellationToken) is { } message)
        {
            claimed.Add(message);
        }

        return claimed;
    }

    public Task<bool> MarkPublishedAsync(Guid messageId, string processorId, CancellationToken cancellationToken = default) =>
        MongoErrors.ExecuteAsync(async () =>
        {
            var result = await context.OutboxMessages.UpdateOneAsync(
                ClaimedBy(messageId, processorId),
                Builders<OutboxMessageDocument>.Update
                    .Set(m => m.Status, OutboxMessageStatus.Published)
                    .Set(m => m.PublishedAt, UtcNow())
                    .Set(m => m.LockedBy, null)
                    .Set(m => m.LockedUntil, null),
                cancellationToken: cancellationToken);
            return result.ModifiedCount == 1;
        });

    public Task<OutboxFailureResult> MarkFailedAsync(
        Guid messageId,
        string processorId,
        string error,
        CancellationToken cancellationToken = default) =>
        MongoErrors.ExecuteAsync(async () =>
        {
            var current = await context.OutboxMessages.Find(ClaimedBy(messageId, processorId)).FirstOrDefaultAsync(cancellationToken);
            if (current is null)
            {
                return new OutboxFailureResult(Updated: false, MovedToFailed: false, RetryCount: 0, NextAttemptAt: null);
            }

            var retryCount = current.RetryCount + 1;
            var movedToFailed = retryCount >= _options.MaxAttempts;
            var nextAttemptAt = UtcNow() + RetryDelay(retryCount);

            var result = await context.OutboxMessages.UpdateOneAsync(
                // Mismo RetryCount que se leyó: si otro proceso lo cambió en el medio, no se pisa.
                Builders<OutboxMessageDocument>.Filter.And(
                    ClaimedBy(messageId, processorId),
                    Builders<OutboxMessageDocument>.Filter.Eq(m => m.RetryCount, current.RetryCount)),
                Builders<OutboxMessageDocument>.Update
                    .Set(m => m.RetryCount, retryCount)
                    .Set(m => m.LastError, Truncate(error))
                    .Set(m => m.Status, movedToFailed ? OutboxMessageStatus.Failed : OutboxMessageStatus.Pending)
                    .Set(m => m.NextAttemptAt, nextAttemptAt)
                    .Set(m => m.LockedBy, null)
                    .Set(m => m.LockedUntil, null),
                cancellationToken: cancellationToken);

            return result.ModifiedCount == 1
                ? new OutboxFailureResult(true, movedToFailed, retryCount, movedToFailed ? null : new DateTimeOffset(nextAttemptAt, TimeSpan.Zero))
                : new OutboxFailureResult(false, false, current.RetryCount, null);
        });

    public Task<bool> RequeueAsync(Guid messageId, CancellationToken cancellationToken = default) =>
        MongoErrors.ExecuteAsync(async () =>
        {
            var result = await context.OutboxMessages.UpdateOneAsync(
                m => m.MessageId == messageId && m.Status == OutboxMessageStatus.Failed,
                Builders<OutboxMessageDocument>.Update
                    .Set(m => m.Status, OutboxMessageStatus.Pending)
                    .Set(m => m.RetryCount, 0)
                    .Set(m => m.NextAttemptAt, UtcNow()),
                cancellationToken: cancellationToken);
            return result.ModifiedCount == 1;
        });

    public Task<OutboxMessage?> FindByIdAsync(Guid messageId, CancellationToken cancellationToken = default) =>
        MongoErrors.ExecuteAsync(async () =>
        {
            var document = await context.OutboxMessages.Find(m => m.MessageId == messageId).FirstOrDefaultAsync(cancellationToken);
            return document is null ? null : ToMessage(document);
        });

    private static FilterDefinition<OutboxMessageDocument> ClaimedBy(Guid messageId, string processorId) =>
        Builders<OutboxMessageDocument>.Filter.Where(m =>
            m.MessageId == messageId && m.Status == OutboxMessageStatus.Processing && m.LockedBy == processorId);

    private TimeSpan RetryDelay(int retryCount)
    {
        var delay = _options.RetryBaseDelay * Math.Pow(2, Math.Max(0, retryCount - 1));
        return delay < _options.MaxRetryDelay ? delay : _options.MaxRetryDelay;
    }

    // Mongo guarda milisegundos: se trunca para que lo leído coincida con lo escrito.
    private DateTime UtcNow()
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMillisecond));
    }

    private static string Truncate(string error) =>
        error.Length <= MaxErrorLength ? error : error[..MaxErrorLength];

    private static OutboxMessageDocument ToDocument(OutboxMessage message) => new()
    {
        MessageId = message.MessageId,
        RequestId = message.RequestId,
        MessageType = message.MessageType,
        Payload = message.Payload,
        CreatedAt = message.CreatedAt.UtcDateTime,
        PublishedAt = message.PublishedAt?.UtcDateTime,
        Status = message.Status,
        RetryCount = message.RetryCount,
        LastError = message.LastError,
        NextAttemptAt = message.NextAttemptAt.UtcDateTime,
        LockedBy = message.LockedBy,
        LockedUntil = message.LockedUntil?.UtcDateTime,
    };

    private static OutboxMessage ToMessage(OutboxMessageDocument document) => new(
        document.MessageId,
        document.RequestId,
        document.MessageType,
        document.Payload,
        Utc(document.CreatedAt),
        document.PublishedAt is { } publishedAt ? Utc(publishedAt) : null,
        document.Status,
        document.RetryCount,
        document.LastError,
        Utc(document.NextAttemptAt),
        document.LockedBy,
        document.LockedUntil is { } lockedUntil ? Utc(lockedUntil) : null);

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero);
}
