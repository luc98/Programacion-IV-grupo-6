namespace NotificationService.Messaging;

public sealed class IdleEventSource : IEventSource
{
    public Task RunAsync(IEventProcessor processor, CancellationToken cancellationToken) => Task.CompletedTask;
}
