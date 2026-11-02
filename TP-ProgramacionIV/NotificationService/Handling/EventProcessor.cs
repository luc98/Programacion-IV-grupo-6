using System.Diagnostics;
using NotificationService.Core;
using NotificationService.Logging;
using NotificationService.Mapping;
using NotificationService.Messaging;

namespace NotificationService.Handling;

public sealed class EventProcessor(
    IEventMapper mapper,
    NotificationHandler handler,
    ILogger<EventProcessor> logger) : IEventProcessor
{
    public const string TraceIdProperty = "TraceId";
    public const string Operation = "ProcesarMensaje";

    public async Task<ProcessingResult> ProcessAsync(IncomingEvent incomingEvent, CancellationToken cancellationToken)
    {
        var traceId = incomingEvent.Properties.GetValueOrDefault(TraceIdProperty) ?? incomingEvent.CorrelationId;

        using var scope = logger.BeginScope(
            "TraceId={TraceId} CorrelationId={CorrelationId} Subject={Subject} DeliveryCount={DeliveryCount}",
            traceId, incomingEvent.CorrelationId, incomingEvent.Subject, incomingEvent.DeliveryCount);

        var stopwatch = Stopwatch.StartNew();
        logger.AuditStart(Operation);

        var result = await TranslateAndHandleAsync(incomingEvent, cancellationToken);

        logger.AuditEnd(Operation, result.Outcome.ToString(), stopwatch.ElapsedMilliseconds);
        return result;
    }

    private async Task<ProcessingResult> TranslateAndHandleAsync(IncomingEvent incomingEvent, CancellationToken cancellationToken)
    {
        if (!mapper.TryMap(incomingEvent, out var request, out var error))
        {
            logger.LogWarning("Mensaje inválido, va a la cola de errores. Motivo: {Error}", error);
            return ProcessingResult.DeadLetter(error);
        }

        return await handler.HandleAsync(request, cancellationToken);
    }
}
