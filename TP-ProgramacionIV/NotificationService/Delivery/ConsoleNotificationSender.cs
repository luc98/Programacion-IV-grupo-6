namespace NotificationService.Delivery;

public sealed class ConsoleNotificationSender(ILogger<ConsoleNotificationSender> logger) : INotificationSender
{
    public Task SendAsync(string recipientId, string text, CancellationToken cancellationToken)
    {
        logger.LogInformation("AVISO para {RecipientId}: {Text}", recipientId, text);
        return Task.CompletedTask;
    }
}
