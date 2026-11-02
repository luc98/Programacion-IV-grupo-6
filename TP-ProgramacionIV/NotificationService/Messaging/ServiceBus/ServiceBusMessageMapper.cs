using System.Globalization;
using Azure.Messaging.ServiceBus;

namespace NotificationService.Messaging.ServiceBus;

public static class ServiceBusMessageMapper
{
    public static IncomingEvent ToIncomingEvent(ServiceBusReceivedMessage message)
    {
        var properties = new Dictionary<string, string>();
        foreach (var (key, value) in message.ApplicationProperties)
        {
            var text = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (text is not null)
            {
                properties[key] = text;
            }
        }

        return new IncomingEvent(
            NullIfBlank(message.MessageId),
            NullIfBlank(message.Subject),
            NullIfBlank(message.CorrelationId),
            message.Body.ToString(),
            properties,
            message.DeliveryCount);
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
