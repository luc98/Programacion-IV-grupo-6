using System.Diagnostics.CodeAnalysis;
using NotificationService.Core;
using NotificationService.Messaging;

namespace NotificationService.Mapping;

public interface IEventMapper
{
    bool TryMap(
        IncomingEvent incomingEvent,
        [NotNullWhen(true)] out NotificationRequest? request,
        [NotNullWhen(false)] out string? error);
}
