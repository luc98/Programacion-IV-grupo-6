using System.Text.Encodings.Web;
using System.Text.Unicode;
using AvailabilitySite.Web.Presentation;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.WebEncoders;

namespace AvailabilitySite.Web.Composition;

// Servicios propios de la capa de presentación (pantallas Razor).
// Los casos de uso, repositorios y la autenticación se registran en Program.cs con
// AddAvailabilityApplication, AddAvailabilityInfrastructure y AddAuthentication.
public static class AvailabilityServiceRegistration
{
    public static WebApplicationBuilder AddAvailabilityPresentation(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<DateTimeDisplay>();
        builder.Services.AddScoped<EventLookup>();

        // Antiforgery automático en todos los POST de formularios (solicitud de reserva y cierre de sesión).
        builder.Services.Configure<MvcOptions>(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));

        // Emitir acentos y ñ tal cual (UTF-8) en lugar de entidades HTML numéricas.
        builder.Services.Configure<WebEncoderOptions>(options =>
            options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

        // Cierre de sesión: como no se guardan los tokens (SaveTokens = false), Keycloak no recibe el
        // id_token_hint y rechaza el logout ("Missing parameters: id_token_hint"). Se le envía el client_id,
        // que Keycloak acepta en su lugar para identificar al cliente y validar la URL de regreso.
        builder.Services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
        {
            var previous = options.Events.OnRedirectToIdentityProviderForSignOut;
            options.Events.OnRedirectToIdentityProviderForSignOut = async context =>
            {
                if (string.IsNullOrEmpty(context.ProtocolMessage.IdTokenHint))
                    context.ProtocolMessage.ClientId = options.ClientId;
                await previous(context);
            };
        });

        return builder;
    }
}
