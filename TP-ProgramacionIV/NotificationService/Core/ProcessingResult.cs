namespace NotificationService.Core;

public sealed record ProcessingResult(ProcessingOutcome Outcome, string? Reason = null)
{
    public static ProcessingResult Completed() => new(ProcessingOutcome.Completed);

    public static ProcessingResult Retry(string reason) => new(ProcessingOutcome.Retry, reason);

    public static ProcessingResult DeadLetter(string reason) => new(ProcessingOutcome.DeadLetter, reason);
}
