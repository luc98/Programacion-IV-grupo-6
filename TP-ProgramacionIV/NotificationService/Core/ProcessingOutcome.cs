namespace NotificationService.Core;

public enum ProcessingOutcome
{
    Completed,
    Retry,
    DeadLetter
}
