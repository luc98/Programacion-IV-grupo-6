namespace NotificationService.Templates;

public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    public Dictionary<string, string> Templates { get; set; } = new();
}
