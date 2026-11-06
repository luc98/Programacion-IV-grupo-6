namespace NotificationService.Core;

// [JUSTIFICACIÓN - Resultado explícito] En lugar de excepciones, cada caso (Completed, Retry, Rejected) lleva su motivo para poder explicar qué pasó.
public sealed record ProcessingResult(ProcessingOutcome Outcome, string? Reason = null)
{
    public static ProcessingResult Completed() => new(ProcessingOutcome.Completed);

    public static ProcessingResult Retry(string reason) => new(ProcessingOutcome.Retry, reason);

    public static ProcessingResult Rejected(string reason) => new(ProcessingOutcome.Rejected, reason);
}
