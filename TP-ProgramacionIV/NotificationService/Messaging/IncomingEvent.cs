namespace NotificationService.Messaging;

public sealed record IncomingEvent(
    string? MessageId,
    string? Subject,
    string? CorrelationId,
    string Body,
    IReadOnlyDictionary<string, string> Properties,
    int DeliveryCount = 1);
