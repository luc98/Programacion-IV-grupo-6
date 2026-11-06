namespace NotificationService.Messaging;

// [JUSTIFICACIÓN - Capa anticorrupción] Evento crudo sin interpretar: es lo único que el núcleo sabe de la llamada recibida.
public sealed record IncomingEvent(
    string? Subject,
    string? CorrelationId,
    string Body,
    IReadOnlyDictionary<string, string> Properties);
