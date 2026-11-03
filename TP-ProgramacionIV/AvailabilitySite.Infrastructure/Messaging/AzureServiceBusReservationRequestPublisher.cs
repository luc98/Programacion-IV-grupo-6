using System.Text.Json;
using Azure.Messaging.ServiceBus;
using AvailabilitySite.Application.Abstractions;
using AvailabilitySite.Application.Reservations;

namespace AvailabilitySite.Infrastructure.Messaging;

public sealed class AzureServiceBusReservationRequestPublisher(IServiceBusMessageSender sender)
    : IReservationRequestPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null
    };

    public Task PublishAsync(
        ReservationRequestedMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var body = JsonSerializer.Serialize(message, JsonOptions);
        var serviceBusMessage = new ServiceBusMessage(body)
        {
            MessageId = message.SolicitudId.ToString(),
            ContentType = "application/json",
            Subject = "ReservationRequested"
        };

        return sender.SendMessageAsync(serviceBusMessage, cancellationToken);
    }
}
