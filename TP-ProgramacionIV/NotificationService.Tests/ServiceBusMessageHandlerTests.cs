using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NotificationService.Core;
using NotificationService.Delivery;
using NotificationService.Messaging;
using NotificationService.Messaging.Fake;
using NotificationService.Messaging.ServiceBus;

namespace NotificationService.Tests;

public class ServiceBusMessageHandlerTests
{
    private readonly RecordingSettlement _settlement = new();
    private readonly ListLogger<ServiceBusMessageHandlerTests> _logger = new();

    private static ServiceBusReceivedMessage Message(string subject = "Creada", string body = "{}") =>
        ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString(body), messageId: "m-1", subject: subject, correlationId: "c-1");

    private static ServiceBusReceivedMessage FromIncoming(IncomingEvent incoming) =>
        ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString(incoming.Body),
            messageId: incoming.MessageId,
            correlationId: incoming.CorrelationId,
            subject: incoming.Subject,
            properties: incoming.Properties.ToDictionary(p => p.Key, p => (object)p.Value),
            deliveryCount: 1);

    private ServiceBusMessageHandler HandlerFor(Func<IncomingEvent, ProcessingResult> decide, List<IncomingEvent>? received = null) =>
        new(new StubProcessor(e =>
        {
            received?.Add(e);
            return decide(e);
        }), _logger);

    [Fact]
    public async Task Un_aviso_resuelto_se_completa()
    {
        await HandlerFor(_ => ProcessingResult.Completed()).HandleAsync(Message(), _settlement, CancellationToken.None);

        Assert.Equal(["Complete"], _settlement.Calls);
    }

    [Fact]
    public async Task Un_reintento_abandona_el_mensaje_para_que_el_broker_lo_vuelva_a_entregar()
    {
        await HandlerFor(_ => ProcessingResult.Retry("sin conexión")).HandleAsync(Message(), _settlement, CancellationToken.None);

        Assert.Equal(["Abandon"], _settlement.Calls);
    }

    [Fact]
    public async Task Un_mensaje_no_procesable_va_a_la_cola_de_errores_con_su_motivo()
    {
        await HandlerFor(_ => ProcessingResult.DeadLetter("sin plantilla")).HandleAsync(Message(), _settlement, CancellationToken.None);

        Assert.Equal(["DeadLetter"], _settlement.Calls);
        Assert.Equal(ServiceBusMessageHandler.DeadLetterReason, _settlement.Reason);
        Assert.Equal("sin plantilla", _settlement.Description);
    }

    [Fact]
    public async Task Un_motivo_muy_largo_se_recorta_para_no_romper_el_limite_del_broker()
    {
        var largo = new string('x', 5000);

        await HandlerFor(_ => ProcessingResult.DeadLetter(largo)).HandleAsync(Message(), _settlement, CancellationToken.None);

        Assert.Equal(ServiceBusMessageHandler.MaxDescriptionLength, _settlement.Description!.Length);
    }

    [Fact]
    public async Task Si_el_procesador_falla_se_registra_y_se_devuelve_para_reintentar()
    {
        await HandlerFor(_ => throw new InvalidOperationException("falla inesperada"))
            .HandleAsync(Message(), _settlement, CancellationToken.None);

        Assert.Equal(["Abandon"], _settlement.Calls);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Una_cancelacion_se_propaga_y_no_resuelve_el_mensaje()
    {
        var handler = HandlerFor(_ => throw new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => handler.HandleAsync(Message(), _settlement, CancellationToken.None));

        Assert.Empty(_settlement.Calls);
    }

    [Fact]
    public async Task El_procesador_recibe_el_mensaje_ya_convertido()
    {
        var received = new List<IncomingEvent>();

        await HandlerFor(_ => ProcessingResult.Completed(), received)
            .HandleAsync(Message(subject: "ReservaCreada", body: """{ "a": 1 }"""), _settlement, CancellationToken.None);

        var incoming = Assert.Single(received);
        Assert.Equal("ReservaCreada", incoming.Subject);
        Assert.Equal("""{ "a": 1 }""", incoming.Body);
        Assert.Equal("c-1", incoming.CorrelationId);
    }

    [Fact]
    public async Task Los_mensajes_de_ejemplo_de_Booking_pasan_por_el_lector_y_el_nucleo_real()
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
        await using var provider = services.BuildServiceProvider();
        var handler = new ServiceBusMessageHandler(provider.GetRequiredService<IEventProcessor>(), _logger);

        var settlements = new List<RecordingSettlement>();
        foreach (var incoming in SampleEvents.BookingStyle())
        {
            var settlement = new RecordingSettlement();
            settlements.Add(settlement);
            await handler.HandleAsync(FromIncoming(incoming), settlement, CancellationToken.None);
        }

        Assert.Equal(2, sender.Sent.Count);
        Assert.Equal(["Complete"], settlements[0].Calls);
        Assert.Equal(["Complete"], settlements[1].Calls);
        Assert.Equal(["Complete"], settlements[2].Calls);
        Assert.Equal(["DeadLetter"], settlements[3].Calls);
        Assert.Contains("JSON", settlements[3].Description);
    }

    private sealed class StubProcessor(Func<IncomingEvent, ProcessingResult> decide) : IEventProcessor
    {
        public Task<ProcessingResult> ProcessAsync(IncomingEvent incomingEvent, CancellationToken cancellationToken) =>
            Task.FromResult(decide(incomingEvent));
    }

    private sealed class RecordingSettlement : IMessageSettlement
    {
        public List<string> Calls { get; } = [];

        public string? Reason { get; private set; }

        public string? Description { get; private set; }

        public Task CompleteAsync(CancellationToken cancellationToken)
        {
            Calls.Add("Complete");
            return Task.CompletedTask;
        }

        public Task AbandonAsync(CancellationToken cancellationToken)
        {
            Calls.Add("Abandon");
            return Task.CompletedTask;
        }

        public Task DeadLetterAsync(string reason, string description, CancellationToken cancellationToken)
        {
            Calls.Add("DeadLetter");
            Reason = reason;
            Description = description;
            return Task.CompletedTask;
        }
    }
}
