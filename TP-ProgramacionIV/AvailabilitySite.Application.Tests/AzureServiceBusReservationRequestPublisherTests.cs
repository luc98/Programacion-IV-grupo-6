using System.Text.Json;
using Azure.Messaging.ServiceBus;
using AvailabilitySite.Application.Reservations;
using AvailabilitySite.Infrastructure.Messaging;
using Xunit;

namespace AvailabilitySite.Application.Tests;

public sealed class AzureServiceBusReservationRequestPublisherTests
{
    [Fact]
    public async Task PublishAsync_ConfiguresMessageAndPreservesContractNames()
    {
        var sender = new RecordingSender();
        var publisher = new AzureServiceBusReservationRequestPublisher(sender);
        var message = new ReservationRequestedMessage(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "user-123");

        await publisher.PublishAsync(message);

        Assert.NotNull(sender.Message);
        Assert.Equal(message.SolicitudId.ToString(), sender.Message.MessageId);
        Assert.Equal("application/json", sender.Message.ContentType);
        Assert.Equal("ReservationRequested", sender.Message.Subject);

        var json = sender.Message.Body.ToString();
        using var document = JsonDocument.Parse(json);
        Assert.Equal(message.SolicitudId, document.RootElement.GetProperty("SolicitudId").GetGuid());
        Assert.Equal(message.EventoId, document.RootElement.GetProperty("EventoId").GetGuid());
        Assert.Equal(message.AsientoId, document.RootElement.GetProperty("AsientoId").GetGuid());
        Assert.Equal(message.UsuarioId, document.RootElement.GetProperty("UsuarioId").GetString());
    }

    [Fact]
    public async Task PublishAsync_PropagatesCancellationToken()
    {
        var sender = new RecordingSender();
        var publisher = new AzureServiceBusReservationRequestPublisher(sender);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            publisher.PublishAsync(new ReservationRequestedMessage(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "user-123"),
                cancellation.Token));
    }

    [Fact]
    public async Task PublishAsync_PropagatesSenderErrors()
    {
        var sender = new RecordingSender
        {
            Error = new InvalidOperationException("send failed")
        };
        var publisher = new AzureServiceBusReservationRequestPublisher(sender);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            publisher.PublishAsync(new ReservationRequestedMessage(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "user-123")));

        Assert.Equal("send failed", exception.Message);
    }

    private sealed class RecordingSender : IServiceBusMessageSender
    {
        public ServiceBusMessage? Message { get; private set; }

        public Exception? Error { get; init; }

        public Task SendMessageAsync(ServiceBusMessage message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Error is not null)
            {
                return Task.FromException(Error);
            }
            Message = message;
            return Task.CompletedTask;
        }
    }
}
