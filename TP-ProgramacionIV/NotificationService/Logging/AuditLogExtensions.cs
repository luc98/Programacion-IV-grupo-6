namespace NotificationService.Logging;

public static class AuditLogExtensions
{
    public static void AuditStart(this ILogger logger, string operation) =>
        logger.LogInformation("{Etapa} {Operacion}", "Inicio", operation);

    public static void AuditEnd(this ILogger logger, string operation, string result, long elapsedMilliseconds) =>
        logger.LogInformation(
            "{Etapa} {Operacion} Resultado={Resultado} DuracionMs={DuracionMs}",
            "Fin", operation, result, elapsedMilliseconds);
}
