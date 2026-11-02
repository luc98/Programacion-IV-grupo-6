using System.Text.Encodings.Web;
using System.Text.Unicode;
using AvailabilitySite.Web.Composition;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.WebEncoders;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
}

builder.Host.UseDefaultServiceProvider((context, options) =>
{
    options.ValidateScopes = context.HostingEnvironment.IsDevelopment();
    options.ValidateOnBuild = context.HostingEnvironment.IsDevelopment();
});

// Add services to the container.
// Antiforgery automático en todos los POST de formularios.
builder.Services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
// Contratos de Application + implementación elegida (hoy mocks; las llamadas reales
// AddAvailabilityInfrastructure / AddAvailabilityApplication están en Composition/AvailabilityServiceRegistration.cs).
builder.AddAvailability();

// Emitir acentos y ñ tal cual (UTF-8) en lugar de entidades HTML numéricas.
builder.Services.Configure<WebEncoderOptions>(o => o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRequestLocalization("es-AR");
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
