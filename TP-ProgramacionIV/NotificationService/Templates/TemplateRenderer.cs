using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using NotificationService.Core;

namespace NotificationService.Templates;

public sealed partial class TemplateRenderer
{
    private const string MissingData = "(sin dato)";

    private readonly Dictionary<string, string> _templates;

    public TemplateRenderer(IOptions<NotificationOptions> options)
    {
        _templates = new Dictionary<string, string>(options.Value.Templates, StringComparer.OrdinalIgnoreCase);
    }

    public bool TryRender(NotificationRequest request, out string text)
    {
        if (!_templates.TryGetValue(request.Type, out var template))
        {
            text = string.Empty;
            return false;
        }

        text = Placeholder().Replace(template, match =>
            request.Data.TryGetValue(match.Groups[1].Value, out var value) ? value : MissingData);
        return true;
    }

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Placeholder();
}
