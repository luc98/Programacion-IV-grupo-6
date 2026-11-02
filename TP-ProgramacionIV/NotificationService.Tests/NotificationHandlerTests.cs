using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotificationService.Core;
using NotificationService.Delivery;
using NotificationService.Handling;
using NotificationService.Templates;

namespace NotificationService.Tests;

public class NotificationHandlerTests
{
    private readonly RecordingSender _sender = new();
    private readonly ListLogger<NotificationHandler> _logger = new();

    private NotificationHandler HandlerWith(INotificationSender? sender = null, IDeduplicationStore? store = null)
    {
        var options = new NotificationOptions();
        options.Templates["Creada"] = "Hola {Nombre}";

        return new NotificationHandler(
            new TemplateRenderer(Options.Create(options)),
            sender ?? _sender,
            store ?? new InMemoryDeduplicationStore(),
            _logger);
    }

    private static NotificationRequest Request(string type = "Creada", string key = "clave-1") =>
        new("usuario-demo", type, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Nombre"] = "Ana" }, key);

    [Fact]
    public async Task Envia_el_aviso_armado_con_la_plantilla()
    {
        var result = await HandlerWith().HandleAsync(Request(), CancellationToken.None);

        Assert.Equal(ProcessingOutcome.Completed, result.Outcome);
        Assert.Equal([("usuario-demo", "Hola Ana")], _sender.Sent);
    }

    [Fact]
    public async Task No_vuelve_a_enviar_un_aviso_repetido()
    {
        var handler = HandlerWith();

        await handler.HandleAsync(Request(), CancellationToken.None);
        var segunda = await handler.HandleAsync(Request(), CancellationToken.None);

        Assert.Equal(ProcessingOutcome.Completed, segunda.Outcome);
        Assert.Single(_sender.Sent);
    }

    [Fact]
    public async Task Claves_distintas_son_avisos_distintos()
    {
        var handler = HandlerWith();

        await handler.HandleAsync(Request(key: "a"), CancellationToken.None);
        await handler.HandleAsync(Request(key: "b"), CancellationToken.None);

        Assert.Equal(2, _sender.Sent.Count);
    }

    [Fact]
    public async Task Un_tipo_sin_plantilla_va_a_la_cola_de_errores_con_su_motivo()
    {
        var result = await HandlerWith().HandleAsync(Request(type: "Desconocido"), CancellationToken.None);

        Assert.Equal(ProcessingOutcome.DeadLetter, result.Outcome);
        Assert.Contains("Desconocido", result.Reason);
        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task Un_tipo_sin_plantilla_se_registra_como_advertencia()
    {
        await HandlerWith().HandleAsync(Request(type: "Desconocido"), CancellationToken.None);

        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("Desconocido"));
    }

    [Fact]
    public async Task Un_tipo_sin_plantilla_no_deja_reservada_la_clave()
    {
        var handler = HandlerWith();

        var primera = await handler.HandleAsync(Request(type: "Desconocido"), CancellationToken.None);
        var segunda = await handler.HandleAsync(Request(type: "Desconocido"), CancellationToken.None);

        Assert.Equal(ProcessingOutcome.DeadLetter, primera.Outcome);
        Assert.Equal(ProcessingOutcome.DeadLetter, segunda.Outcome);
    }

    [Fact]
    public async Task Si_falla_el_envio_pide_reintentar_y_no_lo_da_por_enviado()
    {
        var handler = HandlerWith(sender: new FailingOnceSender());

        var primera = await handler.HandleAsync(Request(), CancellationToken.None);
        var segunda = await handler.HandleAsync(Request(), CancellationToken.None);

        Assert.Equal(ProcessingOutcome.Retry, primera.Outcome);
        Assert.Equal(ProcessingOutcome.Completed, segunda.Outcome);
    }

    private sealed class FailingOnceSender : INotificationSender
    {
        private bool _failed;

        public Task SendAsync(string recipientId, string text, CancellationToken cancellationToken)
        {
            if (!_failed)
            {
                _failed = true;
                throw new InvalidOperationException("sin conexión");
            }

            return Task.CompletedTask;
        }
    }
}
