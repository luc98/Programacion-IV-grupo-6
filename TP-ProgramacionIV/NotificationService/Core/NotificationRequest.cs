namespace NotificationService.Core;

// [JUSTIFICACIÓN - Bajo acoplamiento] Modelo interno del aviso, sin vocabulario de reservas: Type es solo una etiqueta que elige la plantilla,
// así un aviso nuevo es una plantilla más.
public sealed record NotificationRequest(
    string RecipientId,
    string Type,
    IReadOnlyDictionary<string, string> Data,
    string DeduplicationKey,
    string? CorrelationId = null);
