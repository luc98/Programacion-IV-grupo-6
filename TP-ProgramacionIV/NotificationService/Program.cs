using NotificationService;
using NotificationService.Http;
using NotificationService.Security;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// [JUSTIFICACIÓN - Defensa] El cuerpo se limita a 64 KB para que una llamada mal formada no gaste memoria.
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = NotificationEndpoint.MaxBodyBytes);
// [JUSTIFICACIÓN - Configuración externa] Serilog se configura desde appsettings con la plantilla de logs del grupo.
builder.Services.AddSerilog((services, configuration) => configuration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services));
builder.Services.AddNotificationCore(builder.Configuration);

var authenticationEnabled = builder.Configuration.IsAuthenticationEnabled();
if (authenticationEnabled)
{
    builder.Services.AddKeycloakAuthentication(builder.Configuration);
}

var app = builder.Build();
if (authenticationEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.MapNotificationEndpoint(authenticationEnabled);
app.Run();

// [JUSTIFICACIÓN - Pruebas] Hace visible la clase Program para que WebApplicationFactory pueda arrancar la aplicación en las pruebas.
public partial class Program;
