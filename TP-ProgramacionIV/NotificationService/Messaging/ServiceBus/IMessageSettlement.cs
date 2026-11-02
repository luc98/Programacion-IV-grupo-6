namespace NotificationService.Messaging.ServiceBus;

public interface IMessageSettlement
{
    Task CompleteAsync(CancellationToken cancellationToken);

    Task AbandonAsync(CancellationToken cancellationToken);

    Task DeadLetterAsync(string reason, string description, CancellationToken cancellationToken);
}
