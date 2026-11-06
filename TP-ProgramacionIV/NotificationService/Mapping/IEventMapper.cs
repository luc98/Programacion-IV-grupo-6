using System.Diagnostics.CodeAnalysis;
using NotificationService.Core;
using NotificationService.Messaging;

namespace NotificationService.Mapping;

// [JUSTIFICACIÓN - Capa anticorrupción] Si el evento no se entiende, devuelve false con el motivo y no lanza excepciones.
public interface IEventMapper
{
    bool TryMap(
        IncomingEvent incomingEvent,
        [NotNullWhen(true)] out NotificationRequest? request,
        [NotNullWhen(false)] out string? error);
}
