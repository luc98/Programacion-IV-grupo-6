using System.Text;
using Grpc.Net.Client;
using Grupo6.Payment.Contracts.V1;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using PaymentAPI.DAL.Repositories;
using PaymentAPI.Tests.Fakes;

namespace PaymentAPI.Tests.Integracion;

// Levanta Payment entero en memoria (TestServer), con el Program.cs real.
// Cambia solo dos cosas: la base (un repositorio en memoria) y Keycloak (una
// clave de firma propia del test, para emitir tokens sin un Keycloak corriendo).
public class PaymentApiFactory : WebApplicationFactory<Program>
{
    public const string Emisor = "http://keycloak-de-prueba/realms/booking-realm";
    public const long LimiteCentavos = 100_000;

    private static readonly SymmetricSecurityKey ClaveDePrueba =
        new(Encoding.UTF8.GetBytes("clave-de-prueba-solo-para-los-tests-de-payment"));

    public RepositorioEnMemoria Repositorio { get; } = new();

    protected virtual bool ValidarAudiencia => true;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:MigrarAlIniciar"] = "false",
            ["Authentication:Authority"] = "",
            ["Authentication:ValidIssuer"] = Emisor,
            ["Authentication:ValidateAudience"] = ValidarAudiencia.ToString(),
            ["BancoSimulado:LimitePorCobroCentavos"] = LimiteCentavos.ToString(),
            ["BancoSimulado:RetardoMs"] = "0"
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPagoRepository>();
            services.AddSingleton<IPagoRepository>(Repositorio);
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
                options => options.TokenValidationParameters.IssuerSigningKey = ClaveDePrueba);
        });
    }

    public PaymentService.PaymentServiceClient CrearCliente()
    {
        var httpClient = CreateDefaultClient(new ResponseVersionHandler());
        var canal = GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions
        {
            HttpClient = httpClient,
            // Igual que en Booking: una cancelación llega como OperationCanceledException,
            // que es lo que el timeout de Polly reconoce.
            ThrowOperationCanceledOnCancellation = true
        });
        return new PaymentService.PaymentServiceClient(canal);
    }

    // Un token como el que Keycloak le da a Booking con client credentials
    public static string CrearToken(
        string azp = "booking-api",
        string? audiencia = "payment-api",
        string emisor = Emisor,
        DateTime? vence = null,
        SecurityKey? clave = null)
    {
        var expira = vence ?? DateTime.UtcNow.AddMinutes(5);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = emisor,
            Audience = audiencia,
            IssuedAt = expira.AddMinutes(-10),
            NotBefore = expira.AddMinutes(-10),
            Expires = expira,
            Claims = new Dictionary<string, object>
            {
                ["azp"] = azp,
                ["sub"] = $"service-account-{azp}"
            },
            SigningCredentials = new SigningCredentials(clave ?? ClaveDePrueba, SecurityAlgorithms.HmacSha256)
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    // TestServer responde con HTTP/1.1 aunque el pedido sea HTTP/2; el cliente gRPC
    // exige HTTP/2. Es el ajuste que indica la documentación de gRPC para .NET.
    private sealed class ResponseVersionHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            response.Version = request.Version;
            return response;
        }
    }
}

public sealed class PaymentApiSinAudienciaFactory : PaymentApiFactory
{
    protected override bool ValidarAudiencia => false;
}
