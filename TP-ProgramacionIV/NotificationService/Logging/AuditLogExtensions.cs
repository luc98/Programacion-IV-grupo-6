namespace NotificationService.Logging;

// [JUSTIFICACIÓN - Plantilla común de logs, punto 3.f] Inicio y fin de cada operación con Servicio, Etapa, Operacion, TraceId,
// Resultado y DuracionMs. Los demás servicios del grupo la pueden copiar.
public static class AuditLogExtensions
{
    public static void AuditStart(this ILogger logger, string operation) =>
        logger.LogInformation("{Etapa} {Operacion}", "Inicio", operation);

    public static void AuditEnd(this ILogger logger, string operation, string result, long elapsedMilliseconds) =>
        logger.LogInformation(
            "{Etapa} {Operacion} Resultado={Resultado} DuracionMs={DuracionMs}",
            "Fin", operation, result, elapsedMilliseconds);
}
