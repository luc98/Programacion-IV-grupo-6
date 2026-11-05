namespace NotificationService.Delivery;

public interface INotificationSender
{
    Task SendAsync(string recipientId, string text, CancellationToken cancellationToken);
}
