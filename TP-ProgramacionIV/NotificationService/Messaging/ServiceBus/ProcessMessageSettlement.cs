using Azure.Messaging.ServiceBus;

namespace NotificationService.Messaging.ServiceBus;

public sealed class ProcessMessageSettlement(ProcessMessageEventArgs args) : IMessageSettlement
{
    public Task CompleteAsync(CancellationToken cancellationToken) =>
        args.CompleteMessageAsync(args.Message, cancellationToken);

    public Task AbandonAsync(CancellationToken cancellationToken) =>
        args.AbandonMessageAsync(args.Message, cancellationToken: cancellationToken);

    public Task DeadLetterAsync(string reason, string description, CancellationToken cancellationToken) =>
        args.DeadLetterMessageAsync(args.Message, reason, description, cancellationToken);
}
