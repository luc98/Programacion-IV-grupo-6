using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotificationService.Handling;
using NotificationService.Mapping;
using NotificationService.Messaging;
using NotificationService.Templates;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;
using Serilog.Templates;

namespace NotificationService.Tests;

public class SerilogIntegrationTests
{
    private static string? Text(LogEvent logEvent, string name) =>
        logEvent.Properties.TryGetValue(name, out var value) ? (value as ScalarValue)?.Value?.ToString() : null;

    private static async Task<List<LogEvent>> ProcessAsync(IncomingEvent incoming)
    {
        var sink = new CollectingSink();
        var serilog = new LoggerConfiguration().Enrich.FromLogContext().WriteTo.Sink(sink).CreateLogger();
        using var factory = new SerilogLoggerFactory(serilog);

        var templates = new NotificationOptions();
        templates.Templates["Creada"] = "Hola {Nombre}";
        var handler = new NotificationHandler(
            new TemplateRenderer(Options.Create(templates)),
            new RecordingSender(),
            new InMemoryDeduplicationStore(),
            factory.CreateLogger<NotificationHandler>());
        var mapper = new JsonEventMapper(Options.Create(new MappingOptions { RecipientField = "UsuarioId" }));
        var processor = new EventProcessor(mapper, handler, factory.CreateLogger<EventProcessor>());

        await processor.ProcessAsync(incoming, CancellationToken.None);
        return sink.Events;
    }

    private static IncomingEvent Message(Dictionary<string, string> properties) =>
        new("Creada", "solicitud-1", """{ "UsuarioId": "ana", "Nombre": "Ana" }""", properties);

    [Fact]
    public async Task El_TraceId_del_mensaje_llega_como_propiedad_a_todos_los_registros()
    {
        var events = await ProcessAsync(Message(new Dictionary<string, string> { ["TraceId"] = "traza-1" }));

        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal("traza-1", Text(e, "TraceId")));
        Assert.All(events, e => Assert.Equal("solicitud-1", Text(e, "CorrelationId")));
    }

    [Fact]
    public async Task Sin_propiedad_TraceId_se_usa_el_CorrelationId()
    {
        var events = await ProcessAsync(Message(new Dictionary<string, string>()));

        Assert.All(events, e => Assert.Equal("solicitud-1", Text(e, "TraceId")));
    }

    [Fact]
    public async Task El_inicio_y_el_fin_llevan_los_campos_de_auditoria()
    {
        var events = await ProcessAsync(Message(new Dictionary<string, string> { ["TraceId"] = "traza-1" }));

        var inicio = events[0];
        var fin = events[1];
        Assert.Equal("Inicio", Text(inicio, "Etapa"));
        Assert.Equal("ProcesarMensaje", Text(inicio, "Operacion"));
        Assert.Equal("Fin", Text(fin, "Etapa"));
        Assert.Equal("ProcesarMensaje", Text(fin, "Operacion"));
        Assert.Equal("Completed", Text(fin, "Resultado"));
        Assert.True(fin.Properties.ContainsKey("DuracionMs"));
    }

    [Fact]
    public void La_plantilla_de_salida_de_appsettings_muestra_el_TraceId_de_la_propiedad()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();
        var template = configuration["Serilog:WriteTo:0:Args:formatter:template"];
        Assert.False(string.IsNullOrWhiteSpace(template));

        var sink = new CollectingSink();
        var serilog = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        serilog.ForContext("TraceId", "traza-9").ForContext("Servicio", "NotificationService").Information("Hola");

        using var writer = new StringWriter();
        new ExpressionTemplate(template!).Format(sink.Events[0], writer);

        Assert.Contains("[TraceId=traza-9]", writer.ToString());
        Assert.Contains("[NotificationService]", writer.ToString());
    }

    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
