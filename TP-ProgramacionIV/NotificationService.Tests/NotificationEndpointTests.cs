using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NotificationService.Delivery;

namespace NotificationService.Tests;

public class NotificationEndpointTests : IClassFixture<NotificationEndpointTests.NotifierFactory>
{
    private readonly NotifierFactory _factory;

    public NotificationEndpointTests(NotifierFactory factory)
    {
        _factory = factory;
        _factory.Sender.Sent.Clear();
        _factory.Sender.Fail = false;
    }

    private static string Payload(string reservaId, string tipo = "ReservaCreada", string estado = "Retenida") => $$"""
        {
          "tipoEvento": "{{tipo}}",
          "reservaId": "{{reservaId}}",
          "solicitudId": "7d9e4c12-3456-4789-a123-bcdef0123456",
          "usuarioId": "user-123",
          "estado": "{{estado}}",
          "fechaVencimientoUtc": "2026-11-05T18:05:00Z",
          "traceId": "trace-abc-123"
        }
        """;

    private static HttpRequestMessage Request(string body, string? eventType = "ReservaCreada", string? traceId = "trace-abc-123")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/notifications")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (eventType is not null)
        {
            request.Headers.Add("x-event-type", eventType);
        }

        if (traceId is not null)
        {
            request.Headers.Add("x-trace-id", traceId);
        }

        return request;
    }

    [Fact]
    public async Task Un_evento_valido_responde_202_y_envia_el_aviso_al_usuario()
    {
        var response = await _factory.CreateClient().SendAsync(Request(Payload("res-ok-1")));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var (recipient, text) = Assert.Single(_factory.Sender.Sent);
        Assert.Equal("user-123", recipient);
        Assert.Contains("res-ok-1", text);
    }

    [Fact]
    public async Task Sin_header_de_tipo_usa_el_tipo_del_cuerpo()
    {
        var response = await _factory.CreateClient().SendAsync(Request(Payload("res-cuerpo-1"), eventType: null, traceId: null));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Single(_factory.Sender.Sent);
    }

    [Fact]
    public async Task La_confirmacion_usa_su_propia_plantilla()
    {
        var response = await _factory.CreateClient().SendAsync(
            Request(Payload("res-conf-1", "ReservaConfirmada", "Confirmada"), "ReservaConfirmada"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Contains("confirmada", Assert.Single(_factory.Sender.Sent).Text);
    }

    [Fact]
    public async Task El_mismo_evento_repetido_responde_202_pero_no_vuelve_a_avisar()
    {
        var client = _factory.CreateClient();

        var first = await client.SendAsync(Request(Payload("res-dup-1")));
        var second = await client.SendAsync(Request(Payload("res-dup-1")));

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Single(_factory.Sender.Sent);
    }

    [Fact]
    public async Task El_mismo_evento_en_paralelo_avisa_una_sola_vez()
    {
        var client = _factory.CreateClient();

        var responses = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => client.SendAsync(Request(Payload("res-par-1")))));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Accepted, r.StatusCode));
        Assert.Single(_factory.Sender.Sent);
    }

    [Theory]
    [InlineData("esto no es json")]
    [InlineData("")]
    [InlineData("""{"tipoEvento":"ReservaCreada"}""")]
    public async Task Un_cuerpo_inutilizable_responde_400_y_no_envia_nada(string body)
    {
        var response = await _factory.CreateClient().SendAsync(Request(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("error", await response.Content.ReadAsStringAsync());
        Assert.Empty(_factory.Sender.Sent);
    }

    [Fact]
    public async Task Sin_tipo_de_evento_responde_400()
    {
        var response = await _factory.CreateClient().SendAsync(
            Request("""{"usuarioId":"user-123","reservaId":"res-sin-tipo"}""", eventType: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_factory.Sender.Sent);
    }

    [Fact]
    public async Task Un_tipo_sin_plantilla_responde_400()
    {
        var response = await _factory.CreateClient().SendAsync(Request(Payload("res-otro-1", "TipoDesconocido"), "TipoDesconocido"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_factory.Sender.Sent);
    }

    [Fact]
    public async Task Si_falla_el_envio_responde_503_y_un_reintento_si_avisa()
    {
        var client = _factory.CreateClient();
        _factory.Sender.Fail = true;

        var failed = await client.SendAsync(Request(Payload("res-falla-1")));
        _factory.Sender.Fail = false;
        var retried = await client.SendAsync(Request(Payload("res-falla-1")));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, retried.StatusCode);
        Assert.Single(_factory.Sender.Sent);
    }

    [Fact]
    public async Task Solo_acepta_POST()
    {
        var response = await _factory.CreateClient().GetAsync("/api/notifications");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    public sealed class NotifierFactory : WebApplicationFactory<Program>
    {
        public SwitchableSender Sender { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("EventSource:Type", "None");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<INotificationSender>();
                services.AddSingleton<INotificationSender>(Sender);
            });
        }
    }

    public sealed class SwitchableSender : INotificationSender
    {
        private readonly object _lock = new();

        public List<(string RecipientId, string Text)> Sent { get; } = [];

        public volatile bool Fail;

        public Task SendAsync(string recipientId, string text, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new InvalidOperationException("Fallo simulado del envío");
            }

            lock (_lock)
            {
                Sent.Add((recipientId, text));
            }

            return Task.CompletedTask;
        }
    }
}
