namespace NotificationService.Templates;

// [JUSTIFICACIÓN - Patrón Options] Plantillas por tipo de aviso desde appsettings, sección Notifications:Templates.
public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    public Dictionary<string, string> Templates { get; set; } = new();
}
