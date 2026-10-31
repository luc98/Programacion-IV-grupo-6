using BookingAPI.BLL.Gateways;
using BookingAPI.BLL.Interfaces;
using BookingAPI.BLL.Jobs;
using BookingAPI.BLL.Protos;
using BookingAPI.BLL.Services;
using BookingAPI.DAL.Context;
using BookingAPI.DAL.Repositories;
using BookingAPI.Presentation.Listeners;
using Grpc.Net.Client;
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

// [ROMÁN RANAURO - INTEGRACIÓN gRPC Y RESILIENCIA CON POLLY]
// Configuración de canal gRPC y cliente fuertemente tipado hacia Payment gRPC API
builder.Services.AddSingleton(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var paymentUrl = configuration["GrpcSettings:PaymentServiceUrl"] ?? "https://localhost:7001";
    return GrpcChannel.ForAddress(paymentUrl);
});

builder.Services.AddScoped(sp =>
{
    var channel = sp.GetRequiredService<GrpcChannel>();
    return new PaymentService.PaymentServiceClient(channel);
});

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
