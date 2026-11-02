using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NotificationService.Core;
using NotificationService.Messaging;
using NotificationService.Messaging.Fake;
using NotificationService.Messaging.ServiceBus;

namespace NotificationService.Tests;

public class EventSourceSelectionTests
{
    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();

    private static ServiceProvider Build(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNotificationCore(configuration);
        services.AddEventSource(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Sin_configuracion_usa_la_fuente_simulada()
    {
        using var provider = Build(Configuration());

        Assert.IsType<FakeEventSource>(provider.GetRequiredService<IEventSource>());
    }

    [Theory]
    [InlineData("Fake")]
    [InlineData("fake")]
    public void Con_Fake_usa_la_fuente_simulada(string type)
    {
        using var provider = Build(Configuration(("EventSource:Type", type)));

        Assert.IsType<FakeEventSource>(provider.GetRequiredService<IEventSource>());
    }

    [Theory]
    [InlineData("ServiceBus")]
    [InlineData("servicebus")]
    public void Con_ServiceBus_usa_el_lector_sin_conectarse_todavia(string type)
    {
        using var provider = Build(Configuration(("EventSource:Type", type)));

        Assert.IsType<ServiceBusEventSource>(provider.GetRequiredService<IEventSource>());
    }

    [Fact]
    public void Un_tipo_desconocido_falla_al_arrancar_con_los_valores_posibles()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Build(Configuration(("EventSource:Type", "Otra"))));

        Assert.Contains("Fake", ex.Message);
        Assert.Contains("ServiceBus", ex.Message);
    }

    [Fact]
    public async Task El_lector_sin_cadena_de_conexion_falla_con_un_mensaje_claro_antes_de_conectarse()
    {
        var source = new ServiceBusEventSource(
            Options.Create(new ServiceBusOptions { SubscriptionName = "notification" }),
            NullLogger<ServiceBusEventSource>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => source.RunAsync(new NoOpProcessor(), CancellationToken.None));

        Assert.Contains("ServiceBus__ConnectionString", ex.Message);
    }

    private sealed class NoOpProcessor : IEventProcessor
    {
        public Task<ProcessingResult> ProcessAsync(IncomingEvent incomingEvent, CancellationToken cancellationToken) =>
            Task.FromResult(ProcessingResult.Completed());
    }
}
