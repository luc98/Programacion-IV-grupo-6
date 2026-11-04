using Azure.Messaging.ServiceBus;

namespace AvailabilitySite.Infrastructure.Messaging;

public interface IServiceBusMessageSender
{
    Task SendMessageAsync(ServiceBusMessage message, CancellationToken cancellationToken);
}
