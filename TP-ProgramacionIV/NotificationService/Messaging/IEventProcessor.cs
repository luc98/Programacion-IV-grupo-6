using NotificationService.Core;

namespace NotificationService.Messaging;

// [JUSTIFICACIÓN - Inversión de dependencias (DIP)] La entrada conoce solo esta puerta, no el traductor ni el envío.
public interface IEventProcessor
{
    Task<ProcessingResult> ProcessAsync(IncomingEvent incomingEvent, CancellationToken cancellationToken);
}
