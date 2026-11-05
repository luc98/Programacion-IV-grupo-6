namespace NotificationService.Messaging;

public sealed record IncomingEvent(
    string? Subject,
    string? CorrelationId,
    string Body,
    IReadOnlyDictionary<string, string> Properties);
