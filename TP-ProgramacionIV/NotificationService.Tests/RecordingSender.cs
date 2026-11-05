using NotificationService.Delivery;

namespace NotificationService.Tests;

internal sealed class RecordingSender : INotificationSender
{
    private readonly object _lock = new();

    public List<(string RecipientId, string Text)> Sent { get; } = [];

    public Task SendAsync(string recipientId, string text, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            Sent.Add((recipientId, text));
        }

        return Task.CompletedTask;
    }
}
