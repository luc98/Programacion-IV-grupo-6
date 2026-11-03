using System.Text.Json;
using AvailabilitySite.Application.Abstractions;
using AvailabilitySite.Application.Reservations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AvailabilitySite.Infrastructure.Persistence.MongoDb;

namespace AvailabilitySite.Infrastructure.Outbox;

// Despacha mensajes persistidos en Outbox. La entrega es at-least-once: si el proceso
// cae después de publicar y antes de marcar Published, Booking puede recibir un duplicado.
public sealed class OutboxBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxBackgroundService> logger,
    IOptions<OutboxOptions> options) : BackgroundService
{
    private static readonly TimeSpan PollDelay = TimeSpan.FromSeconds(1);
    private readonly string processorId = $"availability-{Guid.NewGuid():N}";
    private readonly TimeSpan leaseDuration = options.Value.LeaseDuration;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = await ProcessOneAsync(stoppingToken);
            if (!processed)
            {
                await Task.Delay(PollDelay, stoppingToken);
            }
        }
    }

    internal async Task<bool> ProcessOneAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var publisher = scope.ServiceProvider.GetRequiredService<IReservationRequestPublisher>();
        var message = await outbox.TryClaimNextAsync(processorId, cancellationToken);
        if (message is null)
        {
            return false;
        }

        using var processingCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var leaseLost = 0;
        var renewalTask = RenewLeaseAsync(outbox, message.MessageId, processingCancellation.Token, () =>
        {
            Interlocked.Exchange(ref leaseLost, 1);
            processingCancellation.Cancel();
        });
        Exception? processingError = null;
        try
        {
            var requested = JsonSerializer.Deserialize<ReservationRequestedMessage>(message.Payload)
                ?? throw new JsonException("El payload del Outbox está vacío.");
            await publisher.PublishAsync(requested, processingCancellation.Token);
            processingCancellation.Cancel();
            await renewalTask;
            if (leaseLost == 1)
            {
                logger.LogWarning("Se perdió el lease del mensaje Outbox {MessageId}; no se marcará como publicado.", message.MessageId);
                return true;
            }
            var published = await outbox.MarkPublishedAsync(message.MessageId, processorId, cancellationToken);
            if (!published)
            {
                logger.LogWarning("No se pudo marcar como publicado el mensaje Outbox {MessageId}; el lease ya no pertenece a este Worker.", message.MessageId);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (leaseLost == 1)
        {
            logger.LogWarning("La publicación se canceló porque el mensaje Outbox {MessageId} ya no pertenece a este Worker.", message.MessageId);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Falló la publicación del mensaje Outbox {MessageId}.", message.MessageId);
            processingError = exception;
        }
        finally
        {
            processingCancellation.Cancel();
            await renewalTask;
        }

        if (processingError is not null)
        {
            await outbox.MarkFailedAsync(message.MessageId, processorId, processingError.Message, cancellationToken);
        }

        return true;
    }

    private async Task RenewLeaseAsync(IOutboxRepository outbox, Guid messageId, CancellationToken cancellationToken, Action leaseLost)
    {
        var interval = leaseDuration <= TimeSpan.FromSeconds(2)
            ? TimeSpan.FromMilliseconds(Math.Max(100, leaseDuration.TotalMilliseconds / 2))
            : TimeSpan.FromTicks(leaseDuration.Ticks / 3);
        try
        {
            while (await Task.Delay(interval, cancellationToken).ContinueWith(t => !t.IsCanceled && !t.IsFaulted, CancellationToken.None))
            {
                if (!await outbox.RenewLeaseAsync(messageId, processorId, leaseDuration, cancellationToken))
                {
                    leaseLost();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Falló la renovación del lease del mensaje Outbox {MessageId}.", messageId);
            leaseLost();
        }
    }
}
