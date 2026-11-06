namespace NotificationService.Core;

// [JUSTIFICACIÓN - Puertos y adaptadores] El núcleo informa el resultado sin conocer el transporte; la entrada HTTP lo traduce a 202, 400 o 503.
public enum ProcessingOutcome
{
    Completed,
    Retry,
    Rejected
}
