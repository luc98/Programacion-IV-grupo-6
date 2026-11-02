using Azure.Messaging.ServiceBus;
using NotificationService.Core;

namespace NotificationService.Messaging.ServiceBus;

public sealed class ServiceBusMessageHandler(IEventProcessor processor, ILogger logger)
{
    public const string DeadLetterReason = "NoProcesable";
    public const int MaxDescriptionLength = 1000;

    public async Task HandleAsync(
        ServiceBusReceivedMessage message,
        IMessageSettlement settlement,
        CancellationToken cancellationToken)
    {
        var incoming = ServiceBusMessageMapper.ToIncomingEvent(message);

        ProcessingResult result;
        try
        {
            result = await processor.ProcessAsync(incoming, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex, "El procesador falló con el mensaje {MessageId}; se devuelve para reintentar.", message.MessageId);
            result = ProcessingResult.Retry($"El procesador falló: {ex.Message}");
        }

        switch (result.Outcome)
        {
            case ProcessingOutcome.Completed:
                await settlement.CompleteAsync(cancellationToken);
                break;
            case ProcessingOutcome.Retry:
                await settlement.AbandonAsync(cancellationToken);
                break;
            default:
                await settlement.DeadLetterAsync(DeadLetterReason, Truncate(result.Reason ?? "sin motivo"), cancellationToken);
                break;
        }
    }

    private static string Truncate(string text) =>
        text.Length <= MaxDescriptionLength ? text : text[..MaxDescriptionLength];
}
