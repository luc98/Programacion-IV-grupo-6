using NotificationService.Core;
using NotificationService.Delivery;
using NotificationService.Templates;

namespace NotificationService.Handling;

public sealed class NotificationHandler(
    TemplateRenderer renderer,
    INotificationSender sender,
    IDeduplicationStore deduplication,
    ILogger<NotificationHandler> logger)
{
    public async Task<ProcessingResult> HandleAsync(NotificationRequest request, CancellationToken cancellationToken)
    {
        if (!deduplication.TryBegin(request.DeduplicationKey))
        {
            logger.LogInformation(
                "Aviso repetido, no se vuelve a enviar (tipo {Type}, clave {Key}).",
                request.Type, request.DeduplicationKey);
            return ProcessingResult.Completed();
        }

        var sent = false;
        try
        {
            if (!renderer.TryRender(request, out var text))
            {
                logger.LogWarning(
                    "No hay plantilla para el tipo de aviso {Type}: el mensaje va a la cola de errores (clave {Key}).",
                    request.Type, request.DeduplicationKey);
                return ProcessingResult.DeadLetter($"No hay plantilla para el tipo de aviso '{request.Type}'.");
            }

            try
            {
                await sender.SendAsync(request.RecipientId, text, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "No se pudo enviar el aviso de tipo {Type}; se va a reintentar.", request.Type);
                return ProcessingResult.Retry($"No se pudo enviar el aviso: {ex.Message}");
            }

            sent = true;
            return ProcessingResult.Completed();
        }
        finally
        {
            if (!sent)
            {
                deduplication.Release(request.DeduplicationKey);
            }
        }
    }
}
