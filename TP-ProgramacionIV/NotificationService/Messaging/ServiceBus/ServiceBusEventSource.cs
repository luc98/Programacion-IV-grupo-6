using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace NotificationService.Messaging.ServiceBus;

public sealed class ServiceBusEventSource(
    IOptions<ServiceBusOptions> options,
    ILogger<ServiceBusEventSource> logger) : IEventSource
{
    public async Task RunAsync(IEventProcessor processor, CancellationToken cancellationToken)
    {
        var settings = ServiceBusSettings.Resolve(options.Value);
        var handler = new ServiceBusMessageHandler(processor, logger);

        await using var client = new ServiceBusClient(settings.ConnectionString);
        await using var busProcessor = client.CreateProcessor(
            settings.TopicName,
            settings.SubscriptionName,
            new ServiceBusProcessorOptions
            {
                AutoCompleteMessages = false,
                MaxConcurrentCalls = settings.MaxConcurrentCalls,
            });

        busProcessor.ProcessMessageAsync += args =>
            handler.HandleAsync(args.Message, new ProcessMessageSettlement(args), args.CancellationToken);
        busProcessor.ProcessErrorAsync += args =>
        {
            logger.LogError(
                args.Exception,
                "Error de Service Bus (origen {ErrorSource}, entidad {EntityPath}).",
                args.ErrorSource, args.EntityPath);
            return Task.CompletedTask;
        };

        await busProcessor.StartProcessingAsync(cancellationToken);
        logger.LogInformation(
            "Escuchando la suscripción {Subscription} del topic {Topic} con {Concurrency} mensaje(s) a la vez.",
            settings.SubscriptionName, settings.TopicName, settings.MaxConcurrentCalls);

        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Se pidió detener el servicio: se deja de recibir mensajes.");
        }
        finally
        {
            await busProcessor.StopProcessingAsync(CancellationToken.None);
        }
    }
}
