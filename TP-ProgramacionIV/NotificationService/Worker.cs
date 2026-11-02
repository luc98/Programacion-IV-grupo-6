using NotificationService.Messaging;

namespace NotificationService;

public class Worker(IEventSource source, IEventProcessor processor, ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Notificador iniciado: esperando mensajes.");

        try
        {
            await source.RunAsync(processor, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogCritical(ex, "La fuente de eventos falló y el servicio se detiene.");
            Environment.ExitCode = 1;
            throw;
        }

        logger.LogInformation("La fuente de eventos terminó; el notificador sigue activo hasta que se lo detenga.");
    }
}
