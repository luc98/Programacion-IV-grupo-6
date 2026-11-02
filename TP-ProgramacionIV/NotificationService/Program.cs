using NotificationService;
using Serilog;

try
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddSerilog((services, configuration) => configuration
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services));
    builder.Services.AddNotificationCore(builder.Configuration);
    builder.Services.AddEventSource(builder.Configuration);
    builder.Services.AddHostedService<Worker>();

    var host = builder.Build();
    host.Run();
    return Environment.ExitCode;
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine($"No se pudo iniciar el servicio: {ex.Message}");
    return 1;
}
