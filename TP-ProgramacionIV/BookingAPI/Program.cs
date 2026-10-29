using BookingAPI.BLL.Interfaces;
using BookingAPI.BLL.Services;
using BookingAPI.DAL.Context;
using BookingAPI.DAL.Repositories;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Auditoria y Trazabilidad
// Configurar para registrar el inicio y fin de la cadena de sucesos con TraceId / CorrelationId.

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Se configura PostgreSQL (Npgsql) como base de datos de Booking REST API.
builder.Services.AddDbContext<BookingDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("BookingDb")));

builder.Services.AddScoped<IReservaRepository, ReservaRepository>();
builder.Services.AddScoped<IBookingService, BookingService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseRouting();

// Falta Authentication

app.UseAuthorization();

app.MapControllers();

app.Run();
