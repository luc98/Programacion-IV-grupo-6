namespace NotificationService.Messaging.Fake;

public sealed record DeadLetteredEvent(IncomingEvent Event, string Reason);
