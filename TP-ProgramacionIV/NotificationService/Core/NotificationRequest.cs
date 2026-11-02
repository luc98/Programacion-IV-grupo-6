namespace NotificationService.Core;

public sealed record NotificationRequest(
    string RecipientId,
    string Type,
    IReadOnlyDictionary<string, string> Data,
    string DeduplicationKey,
    string? CorrelationId = null);
