using NotificationService.Core;

namespace NotificationService.Messaging;

public interface IEventProcessor
{
    Task<ProcessingResult> ProcessAsync(IncomingEvent incomingEvent, CancellationToken cancellationToken);
}
