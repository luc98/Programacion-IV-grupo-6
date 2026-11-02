using NotificationService.Core;

namespace NotificationService.Messaging.Fake;

public sealed class FakeEventSource(
    IEnumerable<IncomingEvent> events,
    ILogger<FakeEventSource> logger,
    TimeSpan? interval = null,
    int maxDeliveryCount = 3) : IEventSource
{
    private readonly List<DeadLetteredEvent> _deadLetters = [];

    public IReadOnlyList<DeadLetteredEvent> DeadLetters => _deadLetters;

    public async Task RunAsync(IEventProcessor processor, CancellationToken cancellationToken)
    {
        var pause = interval ?? TimeSpan.Zero;

        try
        {
            foreach (var original in events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await DeliverAsync(processor, original, cancellationToken);

                if (pause > TimeSpan.Zero)
                {
                    await Task.Delay(pause, cancellationToken);
                }
            }

            logger.LogInformation(
                "Fuente simulada: se entregaron todos los mensajes de ejemplo. En la cola de errores: {Count}.",
                _deadLetters.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
    }

    private async Task DeliverAsync(IEventProcessor processor, IncomingEvent original, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var delivery = original with { DeliveryCount = attempt };
            var result = await ProcessSafelyAsync(processor, delivery, cancellationToken);

            if (result.Outcome == ProcessingOutcome.Completed)
            {
                return;
            }

            if (result.Outcome == ProcessingOutcome.DeadLetter)
            {
                DeadLetter(original, result.Reason ?? "sin motivo");
                return;
            }

            if (attempt >= maxDeliveryCount)
            {
                DeadLetter(original, $"Se superó el máximo de {maxDeliveryCount} entregas. Último motivo: {result.Reason ?? "sin motivo"}");
                return;
            }
        }
    }

    private void DeadLetter(IncomingEvent message, string reason)
    {
        _deadLetters.Add(new DeadLetteredEvent(message, reason));
        logger.LogWarning(
            "Cola de errores: mensaje apartado (Subject {Subject}). Motivo: {Reason}",
            message.Subject, reason);
    }

    private async Task<ProcessingResult> ProcessSafelyAsync(
        IEventProcessor processor, IncomingEvent delivery, CancellationToken cancellationToken)
    {
        try
        {
            return await processor.ProcessAsync(delivery, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Fuente simulada: el procesador falló (Subject {Subject}).", delivery.Subject);
            return ProcessingResult.Retry($"El procesador falló: {ex.Message}");
        }
    }
}
