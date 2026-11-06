namespace NotificationService.Mapping;

// [JUSTIFICACIÓN - Patrón Options] Mapeo configurable desde appsettings, sección Notifications:Mapping.
public sealed class MappingOptions
{
    public const string SectionName = "Notifications:Mapping";

    public string RecipientField { get; set; } = "RecipientId";

    public List<string> DeduplicationFields { get; set; } = [];
}
