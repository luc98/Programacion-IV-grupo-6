using NotificationService;
using NotificationService.Http;
using Serilog;

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = NotificationEndpoint.MaxBodyBytes);
    builder.Services.AddSerilog((services, configuration) => configuration
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services));
    builder.Services.AddNotificationCore(builder.Configuration);
    builder.Services.AddEventSource(builder.Configuration);
    builder.Services.AddHostedService<Worker>();

    var app = builder.Build();
    app.MapNotificationEndpoint();
    app.Run();
    return Environment.ExitCode;
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine($"No se pudo iniciar el servicio: {ex.Message}");
    return 1;
}

public partial class Program;
