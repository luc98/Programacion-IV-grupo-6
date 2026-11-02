using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NotificationService.Core;
using NotificationService.Messaging;
using NotificationService.Messaging.Fake;

namespace NotificationService.Tests;

public class FakeEventSourceTests
{
    private static IncomingEvent Event(string subject) =>
        new(null, subject, null, "{}", new Dictionary<string, string>());

    private static FakeEventSource SourceOf(
        IEnumerable<IncomingEvent> events, int maxDeliveryCount = 3, ILogger<FakeEventSource>? logger = null) =>
        new(events, logger ?? NullLogger<FakeEventSource>.Instance, maxDeliveryCount: maxDeliveryCount);

    [Fact]
    public async Task Entrega_los_mensajes_en_orden()
    {
        var processor = new RecordingProcessor(_ => ProcessingResult.Completed());

        await SourceOf([Event("A"), Event("B"), Event("C")]).RunAsync(processor, CancellationToken.None);

        Assert.Equal(["A", "B", "C"], processor.Received.Select(e => e.Subject));
    }

    [Fact]
    public async Task Ante_un_reintento_vuelve_a_entregar_con_el_contador_en_aumento()
    {
        var processor = new RecordingProcessor(e =>
            e.DeliveryCount < 2 ? ProcessingResult.Retry("falla pasajera") : ProcessingResult.Completed());

        await SourceOf([Event("A")]).RunAsync(processor, CancellationToken.None);

        Assert.Equal([1, 2], processor.Received.Select(e => e.DeliveryCount));
    }

    [Fact]
    public async Task Aparta_el_mensaje_al_llegar_al_maximo_de_entregas()
    {
        var processor = new RecordingProcessor(_ => ProcessingResult.Retry("falla pasajera"));

        await SourceOf([Event("A"), Event("B")], maxDeliveryCount: 2).RunAsync(processor, CancellationToken.None);

        Assert.Equal(["A", "A", "B", "B"], processor.Received.Select(e => e.Subject));
    }

    [Fact]
    public async Task No_reintenta_un_mensaje_apartado()
    {
        var processor = new RecordingProcessor(_ => ProcessingResult.DeadLetter("no sirve"));

        await SourceOf([Event("A")]).RunAsync(processor, CancellationToken.None);

        Assert.Single(processor.Received);
    }

    [Fact]
    public async Task Un_error_del_procesador_se_trata_como_reintento()
    {
        var processor = new RecordingProcessor(e => e.DeliveryCount == 1
            ? throw new InvalidOperationException("falla pasajera")
            : ProcessingResult.Completed());

        await SourceOf([Event("A")]).RunAsync(processor, CancellationToken.None);

        Assert.Equal([1, 2], processor.Received.Select(e => e.DeliveryCount));
    }

    [Fact]
    public async Task El_mensaje_apartado_queda_en_la_cola_de_errores_con_su_motivo()
    {
        var processor = new RecordingProcessor(_ => ProcessingResult.DeadLetter("sin plantilla"));
        var source = SourceOf([Event("A")]);

        await source.RunAsync(processor, CancellationToken.None);

        var apartado = Assert.Single(source.DeadLetters);
        Assert.Equal("A", apartado.Event.Subject);
        Assert.Equal("sin plantilla", apartado.Reason);
    }

    [Fact]
    public async Task Al_superar_el_maximo_de_entregas_va_a_la_cola_de_errores_con_el_ultimo_motivo()
    {
        var processor = new RecordingProcessor(_ => ProcessingResult.Retry("sin conexión"));
        var source = SourceOf([Event("A")], maxDeliveryCount: 2);

        await source.RunAsync(processor, CancellationToken.None);

        var apartado = Assert.Single(source.DeadLetters);
        Assert.Contains("2 entregas", apartado.Reason);
        Assert.Contains("sin conexión", apartado.Reason);
    }

    [Fact]
    public async Task Apartar_un_mensaje_se_registra_como_advertencia()
    {
        var logger = new ListLogger<FakeEventSource>();
        var processor = new RecordingProcessor(_ => ProcessingResult.DeadLetter("sin plantilla"));

        await SourceOf([Event("A")], logger: logger).RunAsync(processor, CancellationToken.None);

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("sin plantilla"));
    }

    private sealed class RecordingProcessor(Func<IncomingEvent, ProcessingResult> decide) : IEventProcessor
    {
        public List<IncomingEvent> Received { get; } = [];

        public Task<ProcessingResult> ProcessAsync(IncomingEvent incomingEvent, CancellationToken cancellationToken)
        {
            Received.Add(incomingEvent);
            return Task.FromResult(decide(incomingEvent));
        }
    }
}
