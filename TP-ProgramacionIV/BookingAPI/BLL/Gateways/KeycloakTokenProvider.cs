using System.Text.Json.Serialization;
using BookingAPI.BLL.Interfaces;

namespace BookingAPI.BLL.Gateways
{
    // [JUSTIFICACIÓN TEÓRICA - TOKEN M2M CON CLIENT CREDENTIALS (PUNTO 3.e)]
    // Booking se autentica ante Payment con su propia identidad de Keycloak (cliente booking-api).
    // El token dura unos minutos, así que se guarda y se reutiliza hasta poco antes de que venza:
    // pedir uno por cada cobro recargaría a Keycloak y le sumaría latencia a cada reserva.
    // Es Singleton para que el token guardado sea uno solo para toda la aplicación.
    public class KeycloakTokenProvider : ITokenProvider
    {
        public const string NombreHttpClient = "keycloak";

        // Se renueva un poco antes del vencimiento para no mandar un token que vence en viaje
        private static readonly TimeSpan MargenDeRenovacion = TimeSpan.FromSeconds(30);

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly TimeProvider _reloj;
        private readonly ILogger<KeycloakTokenProvider> _logger;

        // Un solo pedido a Keycloak a la vez: si llegan varios cobros con el token vencido, todos
        // esperan el mismo pedido. Si falla, fallan juntos en lugar de repetirlo uno detrás de otro.
        private readonly object _candado = new();
        private Task<string>? _pedidoEnCurso;

        // Token y vencimiento viajan juntos en un objeto inmutable: se reemplaza entero, así
        // ningún hilo lee un token nuevo con el vencimiento del anterior.
        private volatile TokenGuardado? _guardado;

        public KeycloakTokenProvider(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            TimeProvider reloj,
            ILogger<KeycloakTokenProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _reloj = reloj;
            _logger = logger;
        }

        public async Task<string> ObtenerTokenAsync(CancellationToken cancellationToken)
        {
            if (Vigente() is string token)
            {
                return token;
            }

            Task<string> pedido;
            lock (_candado)
            {
                // Uno terminado (bien o mal) no se reusa: si el token ya no está vigente, se pide otro
                if (_pedidoEnCurso is null || _pedidoEnCurso.IsCompleted)
                {
                    _pedidoEnCurso = PedirAKeycloakAsync();
                }
                pedido = _pedidoEnCurso;
            }

            try
            {
                // El cancellationToken corta solo la espera de este cobro, no el pedido compartido
                return await pedido.WaitAsync(cancellationToken);
            }
            finally
            {
                if (pedido.IsCompleted)
                {
                    // Terminado, bien o mal, se suelta: el próximo cobro usa el token guardado o,
                    // si falló, vuelve a intentar
                    lock (_candado)
                    {
                        if (ReferenceEquals(_pedidoEnCurso, pedido))
                        {
                            _pedidoEnCurso = null;
                        }
                    }
                }
            }
        }

        private async Task<string> PedirAKeycloakAsync()
        {
            var auth = _configuration.GetSection("Authentication");
            string tokenUrl = auth["TokenUrl"] ?? throw new InvalidOperationException("Falta Authentication:TokenUrl");

            using var formulario = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = auth["ClientId"] ?? throw new InvalidOperationException("Falta Authentication:ClientId"),
                ["client_secret"] = auth["ClientSecret"] ?? throw new InvalidOperationException("Falta Authentication:ClientSecret")
            });

            // El tope de tiempo lo pone el HttpClient con nombre "keycloak" (ver Program.cs)
            HttpClient http = _httpClientFactory.CreateClient(NombreHttpClient);
            using HttpResponseMessage respuesta = await http.PostAsync(tokenUrl, formulario);
            respuesta.EnsureSuccessStatusCode();

            RespuestaToken cuerpo = await respuesta.Content.ReadFromJsonAsync<RespuestaToken>()
                ?? throw new InvalidOperationException("Keycloak respondió sin token");

            _guardado = new TokenGuardado(cuerpo.AccessToken, _reloj.GetUtcNow().AddSeconds(cuerpo.ExpiresIn));
            _logger.LogInformation("[KEYCLOAK] Token de booking-api renovado. Vence en {Segundos} s", cuerpo.ExpiresIn);
            return cuerpo.AccessToken;
        }

        public void Invalidar()
        {
            _guardado = null;
            _logger.LogWarning("[KEYCLOAK] Token descartado: el próximo cobro pide uno nuevo");
        }

        private string? Vigente()
        {
            TokenGuardado? guardado = _guardado;
            return guardado is not null && _reloj.GetUtcNow() < guardado.VenceUtc - MargenDeRenovacion
                ? guardado.Valor
                : null;
        }

        private sealed record TokenGuardado(string Valor, DateTimeOffset VenceUtc);

        private sealed record RespuestaToken(
            [property: JsonPropertyName("access_token")] string AccessToken,
            [property: JsonPropertyName("expires_in")] int ExpiresIn);
    }
}
