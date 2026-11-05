using Azure.Messaging.ServiceBus;

namespace AvailabilitySite.Infrastructure.Messaging;

internal sealed class ServiceBusMessageSender(ServiceBusSender sender) : IServiceBusMessageSender
{
    public Task SendMessageAsync(ServiceBusMessage message, CancellationToken cancellationToken) =>
        sender.SendMessageAsync(message, cancellationToken);
}
