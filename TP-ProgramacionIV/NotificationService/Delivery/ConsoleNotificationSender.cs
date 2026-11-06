namespace NotificationService.Delivery;

// [JUSTIFICACIÓN - Adaptador de salida] Escribe el aviso con ILogger y no con Console, así sale por el mismo log, con el TraceId.
public sealed class ConsoleNotificationSender(ILogger<ConsoleNotificationSender> logger) : INotificationSender
{
    public Task SendAsync(string recipientId, string text, CancellationToken cancellationToken)
    {
        logger.LogInformation("AVISO para {RecipientId}: {Text}", recipientId, text);
        return Task.CompletedTask;
    }
}
