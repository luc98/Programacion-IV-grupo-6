using System.Text.Json;
using NotificationService.Handling;
using NotificationService.Messaging;

namespace NotificationService.Http;

public static class HttpEventMapper
{
    public const string TraceIdHeader = "x-trace-id";
    public const string EventTypeHeader = "x-event-type";
    public const string EventTypeField = "tipoEvento";
    public const string TraceIdField = "traceId";

    public static IncomingEvent ToIncomingEvent(string? eventTypeHeader, string? traceIdHeader, string body)
    {
        var eventType = FirstNotBlank(eventTypeHeader, ReadTextField(body, EventTypeField));
        var traceId = FirstNotBlank(traceIdHeader, ReadTextField(body, TraceIdField));

        var properties = new Dictionary<string, string>();
        if (traceId is not null)
        {
            properties[EventProcessor.TraceIdProperty] = traceId;
        }

        return new IncomingEvent(eventType, traceId, body, properties);
    }

    private static string? FirstNotBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string? ReadTextField(string body, string field)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.NameEquals(field) || string.Equals(property.Name, field, StringComparison.OrdinalIgnoreCase))
                {
                    return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
