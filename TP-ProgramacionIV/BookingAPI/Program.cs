using BookingAPI.BLL.Gateways;
using BookingAPI.BLL.Interfaces;
using BookingAPI.BLL.Jobs;
using BookingAPI.BLL.Options;
using BookingAPI.BLL.Services;
using BookingAPI.DAL.Context;
using BookingAPI.DAL.Repositories;
using BookingAPI.Presentation.Listeners;
using Grpc.Net.Client;
using Grupo6.Payment.Contracts.V1;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Auditoria y Trazabilidad
// Configurar para registrar el inicio y fin de la cadena de sucesos con TraceId / CorrelationId.

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Se configura PostgreSQL (Npgsql) como base de datos de Booking REST API.
builder.Services.AddDbContext<BookingDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("BookingDb")));

// Repositorios y Servicios del Dominio
builder.Services.AddScoped<IReservaRepository, ReservaRepository>();
builder.Services.AddScoped<IBookingService, BookingService>();

// [DUPLA 3 - CLIENTE gRPC DE PAYMENT CON RESILIENCIA (PUNTOS 3.e Y 3.j)]
// Canal y cliente gRPC Singleton: un GrpcChannel mantiene abierta la conexión HTTP/2 y es seguro
// compartirlo entre hilos. Crear uno por cobro abriría una conexión nueva cada vez.
builder.Services.AddSingleton(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    // En Docker: http://payment:8080 (HTTP/2 sin TLS dentro de la red interna)
    var paymentUrl = configuration["GrpcSettings:PaymentServiceUrl"];
    return GrpcChannel.ForAddress(
        string.IsNullOrWhiteSpace(paymentUrl) ? "http://localhost:5260" : paymentUrl,
        new GrpcChannelOptions
        {
            // Una llamada cancelada (por ejemplo, por el timeout de Polly) llega como
            // OperationCanceledException, que es lo que reconoce el timeout de Polly. Sin esto
            // llegaría como RpcException(Cancelled) y el intento vencido no se reintentaría.
            ThrowOperationCanceledOnCancellation = true,

            // Con Payment caído, el canal reintenta conectar en segundo plano con esperas que
            // crecen hasta 2 minutos por defecto, y mientras espera rechaza las llamadas sin
            // intentar. Con un tope de 5 s, cuando Payment vuelve el canal reconecta antes de
            // que termine el corte de 15 s del circuit breaker.
            InitialReconnectBackoff = TimeSpan.FromSeconds(1),
            MaxReconnectBackoff = TimeSpan.FromSeconds(5)
        });
});
builder.Services.AddSingleton(sp => new PaymentService.PaymentServiceClient(sp.GetRequiredService<GrpcChannel>()));

// Pipeline de Polly Singleton: el circuit breaker necesita recordar las fallas entre cobros
builder.Services.Configure<ResilienciaPagoOptions>(builder.Configuration.GetSection(ResilienciaPagoOptions.Seccion));
builder.Services.AddSingleton<ResilienciaPago>();

// Token de Keycloak (client credentials) guardado y reutilizado hasta que vence
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient(KeycloakTokenProvider.NombreHttpClient, http => http.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddSingleton<ITokenProvider, KeycloakTokenProvider>();

builder.Services.AddScoped<IPaymentGateway, PaymentGrpcGateway>();

// [ROMÁN RANAURO - INTEGRACIÓN CON AZURE SERVICE BUS (PUB/SUB)]
// Publicador de eventos hacia Notification Service (Punto 3.h del TP)
builder.Services.AddSingleton<IEventPublisher, ServiceBusEventPublisher>();

// Listener en segundo plano (BackgroundService) para procesar solicitudes de reserva de Availability Site (Punto 4 del TP)
builder.Services.AddHostedService<ServiceBusListener>();

// [ROMÁN RANAURO - WORKER EN SEGUNDO PLANO PARA EXPIRACIÓN DE RESERVAS TTL]
// Proceso periódico que marca como 'Vencida' reservas retenidas expiradas liberando el asiento (Puntos 7, 8 y 3.g del TP)
builder.Services.AddHostedService<ExpirationWorker>();

// [AUTENTICACIÓN Y AUTORIZACIÓN CON KEYCLOAK - PUNTO 3.e DEL TP]
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Authentication:Authority"];
        options.Audience = builder.Configuration["Authentication:Audience"];
        options.RequireHttpsMetadata = builder.Configuration.GetValue<bool>("Authentication:RequireHttpsMetadata");
    });

var app = builder.Build();

// [DUPLA 3]: el canal y el pipeline de Polly se crean al arrancar. Una URL o un valor de
// ResilienciaPago inválido corta el arranque con un mensaje claro, en lugar de romper recién
// en el primer cobro.
app.Services.GetRequiredService<GrpcChannel>();
app.Services.GetRequiredService<ResilienciaPago>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
