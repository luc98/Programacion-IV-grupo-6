namespace NotificationService.Messaging.ServiceBus;

public sealed class ServiceBusOptions
{
    public const string SectionName = "ServiceBus";

    public string ConnectionString { get; set; } = string.Empty;

    public string TopicName { get; set; } = string.Empty;

    public string SubscriptionName { get; set; } = string.Empty;

    public int MaxConcurrentCalls { get; set; } = 1;
}
