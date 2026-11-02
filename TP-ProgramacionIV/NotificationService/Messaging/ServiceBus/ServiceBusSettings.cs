using Azure.Messaging.ServiceBus;

namespace NotificationService.Messaging.ServiceBus;

public sealed record ServiceBusSettings(
    string ConnectionString,
    string TopicName,
    string SubscriptionName,
    int MaxConcurrentCalls)
{
    public static ServiceBusSettings Resolve(ServiceBusOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException(
                "Falta la cadena de conexión de Service Bus. Definila en la variable de entorno ServiceBus__ConnectionString o con user-secrets.");
        }

        if (string.IsNullOrWhiteSpace(options.SubscriptionName))
        {
            throw new InvalidOperationException("Falta el nombre de la suscripción (ServiceBus:SubscriptionName).");
        }

        if (options.MaxConcurrentCalls < 1)
        {
            throw new InvalidOperationException("ServiceBus:MaxConcurrentCalls tiene que ser 1 o más.");
        }

        var topic = options.TopicName;
        if (string.IsNullOrWhiteSpace(topic))
        {
            topic = ReadEntityPath(options.ConnectionString);
        }

        if (string.IsNullOrWhiteSpace(topic))
        {
            throw new InvalidOperationException(
                "Falta el nombre del topic: indicalo en ServiceBus:TopicName o incluí EntityPath en la cadena de conexión.");
        }

        return new ServiceBusSettings(options.ConnectionString, topic, options.SubscriptionName, options.MaxConcurrentCalls);
    }

    public override string ToString() => $"{TopicName}/{SubscriptionName}";

    private static string ReadEntityPath(string connectionString)
    {
        try
        {
            return ServiceBusConnectionStringProperties.Parse(connectionString).EntityPath ?? string.Empty;
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("La cadena de conexión de Service Bus no tiene un formato válido.");
        }
    }
}
