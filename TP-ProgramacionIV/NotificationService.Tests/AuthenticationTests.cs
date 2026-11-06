using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using NotificationService.Delivery;

namespace NotificationService.Tests;

public class AuthenticationTests : IClassFixture<AuthenticationTests.SecuredFactory>
{
    private const string Issuer = "http://keycloak.test/realms/reservas-realm";
    private readonly SecuredFactory _factory;

    public AuthenticationTests(SecuredFactory factory)
    {
        _factory = factory;
        _factory.Sender.Sent.Clear();
    }

    private string Token(string client) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = new Dictionary<string, object> { ["azp"] = client },
            SigningCredentials = new SigningCredentials(_factory.Key, SecurityAlgorithms.RsaSha256),
        });

    private static HttpRequestMessage Request(string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/notifications")
        {
            Content = new StringContent(
                """{"tipoEvento":"ReservaCreada","reservaId":"auth-1","usuarioId":"user-1","fechaVencimientoUtc":"2026-11-05T18:05:00Z"}""",
                Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("x-event-type", "ReservaCreada");
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    [Fact]
    public async Task Sin_token_responde_401_y_no_avisa()
    {
        var response = await _factory.CreateClient().SendAsync(Request(null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_factory.Sender.Sent);
    }

    [Fact]
    public async Task Con_un_token_valido_de_booking_responde_202_y_avisa()
    {
        var response = await _factory.CreateClient().SendAsync(Request(Token("booking-api")));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Single(_factory.Sender.Sent);
    }

    [Fact]
    public async Task Con_un_token_valido_de_otro_cliente_responde_403_y_no_avisa()
    {
        var response = await _factory.CreateClient().SendAsync(Request(Token("otro-cliente")));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_factory.Sender.Sent);
    }

    public sealed class SecuredFactory : WebApplicationFactory<Program>
    {
        public RsaSecurityKey Key { get; } = new(RSA.Create(2048)) { KeyId = "clave-de-prueba" };

        public NotificationEndpointTests.SwitchableSender Sender { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Authentication:Enabled", "true");
            builder.UseSetting("Authentication:ValidIssuer", Issuer);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<INotificationSender>();
                services.AddSingleton<INotificationSender>(Sender);
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                    configuration.SigningKeys.Add(Key);
                    options.Configuration = configuration;
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                });
            });
        }
    }
}
