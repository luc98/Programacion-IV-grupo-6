using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace NotificationService.Security;

// [JUSTIFICACIÓN - Seguridad opcional (punto 3.e)] Valida el token Bearer de Keycloak (firma, emisor y vencimiento) y que lo pida un cliente permitido.
// Está apagada por defecto para correr el servicio y sus pruebas sin Keycloak. El compose la activa.
public static class AuthenticationExtensions
{
    public const string EnabledKey = "Authentication:Enabled";
    public const string PolicyName = "SoloClientesPermitidos";

    public static bool IsAuthenticationEnabled(this IConfiguration configuration) =>
        configuration.GetValue(EnabledKey, false);

    public static IServiceCollection AddKeycloakAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var auth = configuration.GetSection("Authentication");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = auth["Authority"];
                options.RequireHttpsMetadata = auth.GetValue("RequireHttpsMetadata", false);
                options.MapInboundClaims = false;

                var issuer = auth["ValidIssuer"];
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ValidateIssuer = true,
                    ValidIssuer = string.IsNullOrWhiteSpace(issuer) ? auth["Authority"] : issuer,
                    ValidateAudience = auth.GetValue("ValidateAudience", false),
                    ValidAudience = auth["Audience"],
                };

                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                            .CreateLogger("Authentication")
                            .LogWarning("Token rechazado ({Tipo})", context.Exception.GetType().Name);
                        return Task.CompletedTask;
                    },
                };
            });

        // [JUSTIFICACIÓN - Autorización por cliente] Además de un token válido, el claim azp (el cliente de Keycloak) tiene que estar en AllowedClients.
        services.AddAuthorizationBuilder()
            .AddPolicy(PolicyName, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim("azp", auth.GetSection("AllowedClients").Get<string[]>() ?? ["booking-api"]));

        return services;
    }
}
