namespace NotificationService.Delivery;

// [JUSTIFICACIÓN - Puertos y adaptadores (DIP)] Puerto de salida: hoy consola, mañana mail o push, sin tocar el núcleo.
public interface INotificationSender
{
    Task SendAsync(string recipientId, string text, CancellationToken cancellationToken);
}
