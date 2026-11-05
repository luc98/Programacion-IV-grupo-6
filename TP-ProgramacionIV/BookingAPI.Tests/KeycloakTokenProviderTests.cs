using System.Net;
using System.Text;
using BookingAPI.BLL.Gateways;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookingAPI.Tests;

public class KeycloakTokenProviderTests
{
    private sealed class RelojManual : TimeProvider
    {
        public DateTimeOffset Ahora { get; set; } = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    // Simula el endpoint de token de Keycloak
    private sealed class KeycloakFalso : HttpMessageHandler
    {
        private int _pedidos;

        public int Pedidos => _pedidos;
        public string? UltimoFormulario { get; private set; }
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public int ExpiraEnSegundos { get; set; } = 300;
        public TimeSpan Demora { get; set; } = TimeSpan.Zero;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var numero = Interlocked.Increment(ref _pedidos);
            UltimoFormulario = await request.Content!.ReadAsStringAsync(cancellationToken);
            await Task.Delay(Demora, cancellationToken);
            return new HttpResponseMessage(Status)
            {
                Content = new StringContent(
                    $"{{\"access_token\":\"token-{numero}\",\"expires_in\":{ExpiraEnSegundos},\"token_type\":\"Bearer\"}}",
                    Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class FabricaFalsa(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private readonly KeycloakFalso _keycloak = new();
    private readonly RelojManual _reloj = new();
    private readonly KeycloakTokenProvider _provider;

    public KeycloakTokenProviderTests()
    {
        var configuracion = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:TokenUrl"] = "http://keycloak-falso/realms/booking-realm/protocol/openid-connect/token",
            ["Authentication:ClientId"] = "booking-api",
            ["Authentication:ClientSecret"] = "secreto-de-prueba"
        }).Build();
        _provider = new KeycloakTokenProvider(
            new FabricaFalsa(_keycloak), configuracion, _reloj, NullLogger<KeycloakTokenProvider>.Instance);
    }

    [Fact]
    public async Task PideElTokenConClientCredentials()
    {
        var token = await _provider.ObtenerTokenAsync(CancellationToken.None);

        Assert.Equal("token-1", token);
        Assert.Contains("grant_type=client_credentials", _keycloak.UltimoFormulario);
        Assert.Contains("client_id=booking-api", _keycloak.UltimoFormulario);
    }

    [Fact]
    public async Task ReutilizaElTokenMientrasEstaVigente()
    {
        await _provider.ObtenerTokenAsync(CancellationToken.None);
        _reloj.Ahora += TimeSpan.FromSeconds(200);
        var segundo = await _provider.ObtenerTokenAsync(CancellationToken.None);

        Assert.Equal("token-1", segundo);
        Assert.Equal(1, _keycloak.Pedidos);
    }

    [Fact]
    public async Task LoRenuevaPocoAntesDeQueVenza()
    {
        await _provider.ObtenerTokenAsync(CancellationToken.None);
        // Vence a los 300 s; a los 275 ya está dentro del margen de 30 s
        _reloj.Ahora += TimeSpan.FromSeconds(275);
        var renovado = await _provider.ObtenerTokenAsync(CancellationToken.None);

        Assert.Equal("token-2", renovado);
        Assert.Equal(2, _keycloak.Pedidos);
    }

    [Fact]
    public async Task Invalidar_HaceQueSePidaUnoNuevo()
    {
        await _provider.ObtenerTokenAsync(CancellationToken.None);
        _provider.Invalidar();
        var nuevo = await _provider.ObtenerTokenAsync(CancellationToken.None);

        Assert.Equal("token-2", nuevo);
    }

    [Fact]
    public async Task VariosCobrosALaVez_HacenUnSoloPedidoAKeycloak()
    {
        _keycloak.Demora = TimeSpan.FromMilliseconds(100);

        var tokens = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => _provider.ObtenerTokenAsync(CancellationToken.None)));

        Assert.All(tokens, t => Assert.Equal("token-1", t));
        Assert.Equal(1, _keycloak.Pedidos);
    }

    [Fact]
    public async Task SiKeycloakFallaConVariosCobrosEsperando_FallanTodosJuntos_ConUnSoloPedido()
    {
        _keycloak.Demora = TimeSpan.FromMilliseconds(100);
        _keycloak.Status = HttpStatusCode.InternalServerError;

        var intentos = Enumerable.Range(0, 10)
            .Select(_ => Assert.ThrowsAsync<HttpRequestException>(() => _provider.ObtenerTokenAsync(CancellationToken.None)))
            .ToArray();
        await Task.WhenAll(intentos);

        Assert.Equal(1, _keycloak.Pedidos);
    }

    [Fact]
    public async Task DespuesDeUnaFalla_ElProximoCobroVuelveAIntentar()
    {
        _keycloak.Status = HttpStatusCode.InternalServerError;
        await Assert.ThrowsAsync<HttpRequestException>(() => _provider.ObtenerTokenAsync(CancellationToken.None));

        _keycloak.Status = HttpStatusCode.OK;
        var token = await _provider.ObtenerTokenAsync(CancellationToken.None);

        Assert.Equal("token-2", token);
    }

    [Fact]
    public async Task LaEsperaDeUnCobroSeCortaConSuToken_SinCancelarElPedido()
    {
        _keycloak.Demora = TimeSpan.FromSeconds(1);
        using var apurado = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _provider.ObtenerTokenAsync(apurado.Token));
        var paciente = await _provider.ObtenerTokenAsync(CancellationToken.None);

        Assert.Equal("token-1", paciente);
        Assert.Equal(1, _keycloak.Pedidos);
    }

    [Fact]
    public async Task SiKeycloakRechazaLasCredenciales_Lanza()
    {
        _keycloak.Status = HttpStatusCode.Unauthorized;

        await Assert.ThrowsAsync<HttpRequestException>(() => _provider.ObtenerTokenAsync(CancellationToken.None));
    }
}
