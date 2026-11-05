using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NotificationService.Core;
using NotificationService.Messaging;

namespace NotificationService.Mapping;

public sealed class JsonEventMapper(IOptions<MappingOptions> options) : IEventMapper
{
    private readonly MappingOptions _options = options.Value;

    public bool TryMap(
        IncomingEvent incomingEvent,
        [NotNullWhen(true)] out NotificationRequest? request,
        [NotNullWhen(false)] out string? error)
    {
        request = null;

        var type = incomingEvent.Subject?.Trim();
        if (string.IsNullOrEmpty(type))
        {
            error = "El mensaje no trae Subject, que es el tipo de aviso.";
            return false;
        }

        if (!TryReadData(incomingEvent, out var data, out error))
        {
            return false;
        }

        if (!data.TryGetValue(_options.RecipientField, out var recipient) || string.IsNullOrWhiteSpace(recipient))
        {
            error = $"El mensaje no trae destinatario (campo '{_options.RecipientField}').";
            return false;
        }

        request = new NotificationRequest(
            recipient, type, data, BuildDeduplicationKey(incomingEvent, type, data), incomingEvent.CorrelationId);
        error = null;
        return true;
    }

    private static bool TryReadData(
        IncomingEvent incomingEvent,
        [NotNullWhen(true)] out Dictionary<string, string>? data,
        [NotNullWhen(false)] out string? error)
    {
        data = null;

        try
        {
            using var document = JsonDocument.Parse(incomingEvent.Body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "El cuerpo del mensaje es un JSON válido, pero no es un objeto.";
                return false;
            }

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                var text = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Null or JsonValueKind.Undefined => null,
                    _ => property.Value.GetRawText(),
                };

                if (text is not null)
                {
                    result[property.Name] = text;
                }
            }

            foreach (var (key, value) in incomingEvent.Properties)
            {
                result.TryAdd(key, value);
            }

            data = result;
            error = null;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"El cuerpo del mensaje no es un JSON válido: {ex.Message}";
            return false;
        }
    }

    private string BuildDeduplicationKey(IncomingEvent incomingEvent, string type, Dictionary<string, string> data)
    {
        if (_options.DeduplicationFields.Count > 0)
        {
            var values = _options.DeduplicationFields
                .Select(field => data.GetValueOrDefault(field))
                .ToList();

            if (values.All(value => !string.IsNullOrWhiteSpace(value)))
            {
                return $"{type}|{string.Join('|', values)}";
            }
        }

        return $"body:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(incomingEvent.Body)))}";
    }
}
