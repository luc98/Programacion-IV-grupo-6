using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using NotificationService.Core;

namespace NotificationService.Templates;

// [JUSTIFICACIÓN - Abierto/cerrado (OCP)] Las plantillas están en la configuración: un tipo de aviso nuevo no obliga a recompilar.
// Un dato faltante se muestra como (sin dato).
public sealed partial class TemplateRenderer
{
    private const string MissingData = "(sin dato)";

    private readonly Dictionary<string, string> _templates;

    public TemplateRenderer(IOptions<NotificationOptions> options)
    {
        _templates = new Dictionary<string, string>(options.Value.Templates, StringComparer.OrdinalIgnoreCase);
    }

    // [JUSTIFICACIÓN - Sin excepciones] Si falta la plantilla devuelve false: el manejador rechaza el aviso con su motivo.
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
