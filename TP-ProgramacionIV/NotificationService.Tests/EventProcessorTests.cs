using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NotificationService.Core;
using NotificationService.Delivery;
using NotificationService.Handling;
using NotificationService.Mapping;
using NotificationService.Messaging;
using NotificationService.Messaging.Fake;
using NotificationService.Templates;

namespace NotificationService.Tests;

public class EventProcessorTests
{
    [Fact]
    public async Task La_composicion_real_procesa_los_mensajes_de_ejemplo()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();
        var sender = new RecordingSender();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNotificationCore(configuration);
        services.AddSingleton<INotificationSender>(sender);
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        var source = new FakeEventSource(SampleEvents.BookingStyle(), NullLogger<FakeEventSource>.Instance);
        await source.RunAsync(provider.GetRequiredService<IEventProcessor>(), CancellationToken.None);

        Assert.Equal(2, sender.Sent.Count);
        Assert.All(sender.Sent, aviso => Assert.Equal("usuario-demo", aviso.RecipientId));
        Assert.Contains("retenida", sender.Sent[0].Text);
        Assert.Contains("confirmada", sender.Sent[1].Text);

        var apartado = Assert.Single(source.DeadLetters);
        Assert.Contains("JSON", apartado.Reason);
    }

    [Fact]
    public async Task Un_mensaje_invalido_se_registra_como_advertencia_con_su_correlacion()
    {
        var logger = new ListLogger<EventProcessor>();
        var invalido = new IncomingEvent(null, "ReservaCreada", "solicitud-7", "esto no es un JSON", new Dictionary<string, string>());

        var result = await ProcessorWith(logger).ProcessAsync(invalido, CancellationToken.None);

        Assert.Equal(ProcessingOutcome.DeadLetter, result.Outcome);
        var advertencia = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("JSON", advertencia.Message);
        Assert.Equal("solicitud-7", ScopeValue(advertencia.Scopes, "CorrelationId"));
    }

    [Fact]
    public async Task Cada_registro_del_procesador_lleva_el_TraceId_y_marca_el_inicio_y_el_fin()
    {
        var logger = new ListLogger<EventProcessor>();
        var invalido = new IncomingEvent(
            null, "ReservaCreada", "solicitud-8", "{", new Dictionary<string, string> { ["TraceId"] = "traza-8" });

        await ProcessorWith(logger).ProcessAsync(invalido, CancellationToken.None);

        Assert.Contains(logger.Entries, e => e.Message == "Inicio ProcesarMensaje");
        Assert.Contains(logger.Entries, e => e.Message.StartsWith("Fin ProcesarMensaje Resultado=DeadLetter DuracionMs="));
        Assert.All(logger.Entries, e => Assert.Equal("traza-8", ScopeValue(e.Scopes, "TraceId")));
        Assert.All(logger.Entries, e => Assert.Equal("solicitud-8", ScopeValue(e.Scopes, "CorrelationId")));
    }

    [Fact]
    public async Task Sin_propiedad_TraceId_el_procesador_usa_el_CorrelationId_como_TraceId()
    {
        var logger = new ListLogger<EventProcessor>();
        var invalido = new IncomingEvent(null, "ReservaCreada", "solicitud-9", "{", new Dictionary<string, string>());

        await ProcessorWith(logger).ProcessAsync(invalido, CancellationToken.None);

        Assert.All(logger.Entries, e => Assert.Equal("solicitud-9", ScopeValue(e.Scopes, "TraceId")));
    }

    private static EventProcessor ProcessorWith(ILogger<EventProcessor> logger)
    {
        var handler = new NotificationHandler(
            new TemplateRenderer(Options.Create(new NotificationOptions())),
            new RecordingSender(),
            new InMemoryDeduplicationStore(),
            NullLogger<NotificationHandler>.Instance);

        var mapper = new JsonEventMapper(Options.Create(new MappingOptions { RecipientField = "UsuarioId" }));
        return new EventProcessor(mapper, handler, logger);
    }

    private static string? ScopeValue(IReadOnlyList<object> scopes, string key) =>
        scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>()
            .SelectMany(scope => scope)
            .Where(pair => pair.Key == key)
            .Select(pair => pair.Value as string)
            .FirstOrDefault();
}
