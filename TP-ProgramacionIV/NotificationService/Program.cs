using NotificationService;
using NotificationService.Http;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// [JUSTIFICACIÓN - Defensa] El cuerpo se limita a 64 KB para que una llamada mal formada no gaste memoria.
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = NotificationEndpoint.MaxBodyBytes);
// [JUSTIFICACIÓN - Configuración externa] Serilog se configura desde appsettings con la plantilla de logs del grupo.
builder.Services.AddSerilog((services, configuration) => configuration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services));
builder.Services.AddNotificationCore(builder.Configuration);

var app = builder.Build();
app.MapNotificationEndpoint();
app.Run();

// [JUSTIFICACIÓN - Pruebas] Hace visible la clase Program para que WebApplicationFactory pueda arrancar la aplicación en las pruebas.
public partial class Program;
