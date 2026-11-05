using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog.Context;
using PaymentAPI.Presentation.Interceptors;

namespace PaymentAPI.Presentation.Seguridad;

// Punto 3.e: Payment solo atiende a quien traiga un token válido de Keycloak
// emitido para Booking (flujo client credentials, máquina a máquina).
public static class AutenticacionExtensions
{
    public const string PoliticaSoloBooking = "SoloBooking";

    public static IServiceCollection AddAutenticacionKeycloak(this IServiceCollection services, IConfiguration configuration)
    {
        var auth = configuration.GetSection("Authentication");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // De acá se bajan las claves públicas del realm. Dentro de Docker es
                // http://keycloak:8080, el nombre del contenedor.
                options.Authority = auth["Authority"];
                options.RequireHttpsMetadata = auth.GetValue("RequireHttpsMetadata", true);

                // Deja los claims con su nombre original (azp, sub) en lugar de
                // traducirlos a los nombres largos de Microsoft.
                options.MapInboundClaims = false;

                var emisor = auth["ValidIssuer"];
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    // Firma, emisor y vencimiento se validan siempre
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ValidateIssuer = true,
                    // El emisor es el que figura en el token. Con KC_HOSTNAME=http://localhost:8080
                    // Keycloak firma como localhost aunque Payment lo consulte por keycloak:8080.
                    ValidIssuer = string.IsNullOrWhiteSpace(emisor) ? auth["Authority"] : emisor,

                    // La audiencia depende de un mapper en el realm del grupo. Mientras no
                    // exista se puede apagar con Authentication__ValidateAudience=false
                    // (decisión 14); la política de abajo sigue exigiendo que el token sea de Booking.
                    ValidateAudience = auth.GetValue("ValidateAudience", true),
                    ValidAudience = auth["Audience"]
                };

                // Sin esto, un 401 no deja rastro en los logs y cuesta saber por qué
                // falló (emisor, audiencia, vencimiento).
                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        using (LogContext.PushProperty(TraceIdServerInterceptor.PropertyName, TraceIdDe(context.HttpContext)))
                        {
                            Logger(context.HttpContext).LogWarning(
                                "Token rechazado ({Tipo}): {Motivo}",
                                context.Exception.GetType().Name, context.Exception.Message);
                        }
                        return Task.CompletedTask;
                    },
                    // Sin header authorization, JwtBearer no dispara OnAuthenticationFailed:
                    // sin esto, una llamada sin token no deja ninguna línea en el log.
                    OnChallenge = context =>
                    {
                        if (context.AuthenticateFailure is null)
                        {
                            using (LogContext.PushProperty(TraceIdServerInterceptor.PropertyName, TraceIdDe(context.HttpContext)))
                            {
                                Logger(context.HttpContext).LogWarning(
                                    "Llamada sin token Bearer a {Ruta}", context.Request.Path.Value);
                            }
                        }
                        return Task.CompletedTask;
                    },
                    OnForbidden = context =>
                    {
                        using (LogContext.PushProperty(TraceIdServerInterceptor.PropertyName, TraceIdDe(context.HttpContext)))
                        {
                            Logger(context.HttpContext).LogWarning(
                                "Token válido, pero el cliente {Cliente} no está autorizado a cobrar",
                                context.HttpContext.User.FindFirst("azp")?.Value ?? "(sin azp)");
                        }
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(PoliticaSoloBooking, policy => policy
                .RequireAuthenticatedUser()
                // azp es el cliente de Keycloak que pidió el token. Así otro cliente
                // del mismo realm, aunque tenga un token válido, no puede cobrar.
                .RequireClaim("azp", auth.GetSection("AllowedClients").Get<string[]>() ?? ["booking-api"]));

        return services;
    }

    private static ILogger Logger(HttpContext httpContext) =>
        httpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("PaymentAPI.Seguridad");

    private static string TraceIdDe(HttpContext httpContext) =>
        httpContext.Request.Headers[TraceIdServerInterceptor.MetadataKey].FirstOrDefault() ?? string.Empty;
}
