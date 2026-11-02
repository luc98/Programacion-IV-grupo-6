using Azure.Messaging.ServiceBus;
using NotificationService.Messaging.ServiceBus;

namespace NotificationService.Tests;

public class ServiceBusMessageMapperTests
{
    [Fact]
    public void Convierte_el_mensaje_del_broker_al_mensaje_crudo()
    {
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("""{ "UsuarioId": "ana" }"""),
            messageId: "m-1",
            correlationId: "c-1",
            subject: "ReservaCreada",
            properties: new Dictionary<string, object> { ["TraceId"] = "t-1", ["Intentos"] = 3 },
            deliveryCount: 2);

        var incoming = ServiceBusMessageMapper.ToIncomingEvent(message);

        Assert.Equal("m-1", incoming.MessageId);
        Assert.Equal("c-1", incoming.CorrelationId);
        Assert.Equal("ReservaCreada", incoming.Subject);
        Assert.Equal("""{ "UsuarioId": "ana" }""", incoming.Body);
        Assert.Equal("t-1", incoming.Properties["TraceId"]);
        Assert.Equal("3", incoming.Properties["Intentos"]);
        Assert.Equal(2, incoming.DeliveryCount);
    }

    [Fact]
    public void Los_campos_que_el_emisor_no_fija_quedan_nulos()
    {
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(body: BinaryData.FromString("{}"));

        var incoming = ServiceBusMessageMapper.ToIncomingEvent(message);

        Assert.Null(incoming.MessageId);
        Assert.Null(incoming.CorrelationId);
        Assert.Null(incoming.Subject);
        Assert.Empty(incoming.Properties);
    }

    [Fact]
    public void Conserva_los_acentos_y_los_decimales_sin_depender_de_la_cultura()
    {
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("""{ "Texto": "¡Gracias! ñandú" }"""),
            properties: new Dictionary<string, object> { ["Monto"] = 12.5 });

        var incoming = ServiceBusMessageMapper.ToIncomingEvent(message);

        Assert.Contains("¡Gracias! ñandú", incoming.Body);
        Assert.Equal("12.5", incoming.Properties["Monto"]);
    }
}
