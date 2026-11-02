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

public class ConcurrencyTests
{
    private static EventProcessor ProcessorWith(INotificationSender sender)
    {
        var templates = new NotificationOptions();
        templates.Templates["Creada"] = "Hola {Nombre}";

        var handler = new NotificationHandler(
            new TemplateRenderer(Options.Create(templates)),
            sender,
            new InMemoryDeduplicationStore(),
            NullLogger<NotificationHandler>.Instance);

        var mapper = new JsonEventMapper(Options.Create(new MappingOptions
        {
            RecipientField = "UsuarioId",
            DeduplicationFields = ["ReservaId"],
        }));

        return new EventProcessor(mapper, handler, NullLogger<EventProcessor>.Instance);
    }

    private static IncomingEvent Message(int reserva) =>
        new(null, "Creada", $"solicitud-{reserva}",
            $$"""{ "UsuarioId": "usuario-{{reserva}}", "ReservaId": "r-{{reserva}}", "Nombre": "Ana" }""",
            new Dictionary<string, string>());

    [Fact]
    public async Task Procesa_muchos_mensajes_distintos_uno_tras_otro()
    {
        var sender = new RecordingSender();
        var messages = Enumerable.Range(0, 2000).Select(Message).ToList();
        var source = new FakeEventSource(messages, NullLogger<FakeEventSource>.Instance);

        await source.RunAsync(ProcessorWith(sender), CancellationToken.None);

        Assert.Equal(2000, sender.Sent.Count);
        Assert.Equal(2000, sender.Sent.Select(s => s.RecipientId).Distinct().Count());
        Assert.Empty(source.DeadLetters);
    }

    [Fact]
    public async Task Procesa_muchos_mensajes_distintos_a_la_vez()
    {
        var sender = new RecordingSender();
        var processor = ProcessorWith(sender);

        await Parallel.ForEachAsync(
            Enumerable.Range(0, 2000),
            new ParallelOptions { MaxDegreeOfParallelism = 32 },
            async (reserva, token) =>
            {
                var result = await processor.ProcessAsync(Message(reserva), token);
                Assert.Equal(ProcessingOutcome.Completed, result.Outcome);
            });

        Assert.Equal(2000, sender.Sent.Count);
        Assert.Equal(2000, sender.Sent.Select(s => s.RecipientId).Distinct().Count());
    }

    [Fact]
    public async Task No_duplica_el_aviso_si_el_mismo_mensaje_llega_varias_veces_a_la_vez()
    {
        var sender = new SlowSender();
        var processor = ProcessorWith(sender);
        var repetido = Message(7);

        var results = await Task.WhenAll(
            Enumerable.Range(0, 100).Select(_ => processor.ProcessAsync(repetido, CancellationToken.None)));

        Assert.All(results, r => Assert.Equal(ProcessingOutcome.Completed, r.Outcome));
        Assert.Single(sender.Inner.Sent);
    }

    private sealed class SlowSender : INotificationSender
    {
        public RecordingSender Inner { get; } = new();

        public async Task SendAsync(string recipientId, string text, CancellationToken cancellationToken)
        {
            await Task.Delay(20, cancellationToken);
            await Inner.SendAsync(recipientId, text, cancellationToken);
        }
    }
}
