namespace NotificationService.Messaging;

public interface IEventSource
{
    Task RunAsync(IEventProcessor processor, CancellationToken cancellationToken);
}
