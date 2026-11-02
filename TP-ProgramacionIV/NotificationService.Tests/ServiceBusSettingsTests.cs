using NotificationService.Messaging.ServiceBus;

namespace NotificationService.Tests;

public class ServiceBusSettingsTests
{
    private static ServiceBusOptions Options(
        string connection = "valor-de-prueba", string topic = "", string subscription = "notification", int calls = 1) =>
        new()
        {
            ConnectionString = connection,
            TopicName = topic,
            SubscriptionName = subscription,
            MaxConcurrentCalls = calls,
        };

    [Fact]
    public void Sin_cadena_de_conexion_explica_donde_definirla()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ServiceBusSettings.Resolve(Options(connection: "")));

        Assert.Contains("ServiceBus__ConnectionString", ex.Message);
    }

    [Fact]
    public void Sin_suscripcion_falla_con_un_mensaje_claro()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ServiceBusSettings.Resolve(Options(topic: "t", subscription: "")));

        Assert.Contains("SubscriptionName", ex.Message);
    }

    [Fact]
    public void El_topic_configurado_tiene_prioridad()
    {
        var settings = ServiceBusSettings.Resolve(Options(topic: "topic-configurado"));

        Assert.Equal("topic-configurado", settings.TopicName);
        Assert.Equal("notification", settings.SubscriptionName);
    }

    [Fact]
    public void Si_no_hay_topic_lo_toma_del_EntityPath_de_la_cadena()
    {
        var settings = ServiceBusSettings.Resolve(
            Options(connection: "Endpoint=sb://ejemplo.servicebus.windows.net/;EntityPath=topic-del-grupo"));

        Assert.Equal("topic-del-grupo", settings.TopicName);
    }

    [Fact]
    public void Sin_topic_ni_EntityPath_falla()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ServiceBusSettings.Resolve(Options(connection: "Endpoint=sb://ejemplo.servicebus.windows.net/")));

        Assert.Contains("topic", ex.Message);
    }

    [Fact]
    public void Una_cadena_con_formato_invalido_falla_sin_repetirla_en_el_mensaje()
    {
        const string invalida = "esto no es una cadena";

        var ex = Assert.Throws<InvalidOperationException>(() => ServiceBusSettings.Resolve(Options(connection: invalida)));

        Assert.DoesNotContain(invalida, ex.Message);
    }

    [Fact]
    public void La_concurrencia_tiene_que_ser_al_menos_uno()
    {
        Assert.Throws<InvalidOperationException>(() => ServiceBusSettings.Resolve(Options(topic: "t", calls: 0)));
    }

    [Fact]
    public void Mostrar_la_configuracion_nunca_revela_la_cadena_de_conexion()
    {
        var settings = ServiceBusSettings.Resolve(Options(connection: "secreto-de-prueba", topic: "t"));

        Assert.DoesNotContain("secreto-de-prueba", settings.ToString());
        Assert.Equal("t/notification", settings.ToString());
    }
}
