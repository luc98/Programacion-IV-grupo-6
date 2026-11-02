using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using PaymentAPI.BLL.Interfaces;
using PaymentAPI.BLL.Options;
using PaymentAPI.BLL.Services;
using PaymentAPI.DAL.Context;
using PaymentAPI.DAL.Repositories;
using PaymentAPI.Presentation.Interceptors;
using PaymentAPI.Presentation.Seguridad;
using PaymentAPI.Presentation.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Logs con TraceId, con la plantilla de dupla 4 (punto 3.f)
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services));

// gRPC sobre HTTP/2. Kestrel escucha solo HTTP/2 (ver appsettings.json): así
// acepta HTTP/2 sin TLS (h2c), que es lo que usa Booking dentro de Docker.
builder.Services.AddGrpc(options => options.Interceptors.Add<TraceIdServerInterceptor>());

// N-Layer: cada capa se registra por separado y depende solo de la de abajo.
// DAL
builder.Services.AddDbContext<PaymentDbContext>(options => options
    .UseNpgsql(builder.Configuration.GetConnectionString("PaymentDb"))
    // EF registra como Error todo comando que falla, aunque el código lo maneje: el
    // choque esperado con el índice UNIQUE en una carrera, o la primera lectura del
    // historial de migraciones en una base vacía. Los errores reales los registran
    // PaymentGrpcService y el interceptor, con el TraceId.
    .ConfigureWarnings(warnings => warnings.Log(
        (RelationalEventId.CommandError, LogLevel.Debug),
        (CoreEventId.SaveChangesFailed, LogLevel.Debug))));
builder.Services.AddScoped<IPagoRepository, PagoRepository>();

// BLL
builder.Services.Configure<BancoSimuladoOptions>(builder.Configuration.GetSection(BancoSimuladoOptions.Seccion));
builder.Services.AddSingleton<IBancoSimulado, BancoSimulado>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IPagoService, PagoService>();

// Seguridad (punto 3.e)
builder.Services.AddAutenticacionKeycloak(builder.Configuration);

var app = builder.Build();

// Payment crea y actualiza su propia base al arrancar. Con una sola réplica es lo
// más simple; con varias, las migraciones se correrían aparte del despliegue.
if (app.Configuration.GetValue("Database:MigrarAlIniciar", true))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<PaymentDbContext>().Database.MigrateAsync();
}

var banco = app.Services.GetRequiredService<IOptions<BancoSimuladoOptions>>().Value;
app.Logger.LogInformation(
    "Banco simulado: límite por cobro {Limite} centavos, usuarios sin fondos [{SinFondos}], retardo {RetardoMs} ms",
    banco.LimitePorCobroCentavos, string.Join(", ", banco.UsuariosSinFondos), banco.RetardoMs);
if (!app.Configuration.GetValue("Authentication:ValidateAudience", true))
{
    app.Logger.LogWarning("La validación de audiencia está apagada: se aceptan tokens de Booking sin aud=payment-api");
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGrpcService<PaymentGrpcService>().RequireAuthorization(AutenticacionExtensions.PoliticaSoloBooking);

app.Run();
