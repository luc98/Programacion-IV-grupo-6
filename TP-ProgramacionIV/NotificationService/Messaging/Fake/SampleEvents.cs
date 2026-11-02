namespace NotificationService.Messaging.Fake;

public static class SampleEvents
{
    private const string SolicitudId = "1b4e28ba-2fa1-11d2-883f-0016d3cca427";
    private const string ReservaId = "7c9e6679-7425-40de-944b-e07fc1f90ae7";

    public static IReadOnlyList<IncomingEvent> BookingStyle()
    {
        var creada = Build("ReservaCreada", "Retenida", "a1f3c0de5b7e4c9f8a2d6e1b3c4d5e6f");
        var confirmada = Build("ReservaConfirmada", "Confirmada", "b2e4d1ef6c8f5dac9b3e7f2c4d5e6f70");

        return
        [
            creada,
            confirmada,
            creada,
            new IncomingEvent(null, "ReservaCreada", SolicitudId, "esto no es un JSON", new Dictionary<string, string>()),
        ];
    }

    private static IncomingEvent Build(string tipo, string estado, string traceId)
    {
        var body = $$"""
            {
              "TipoEvento": "{{tipo}}",
              "ReservaId": "{{ReservaId}}",
              "SolicitudId": "{{SolicitudId}}",
              "AsientoId": "3f2504e0-4f89-11d3-9a0c-0305e82c3301",
              "EventoId": "6ba7b810-9dad-11d1-80b4-00c04fd430c8",
              "UsuarioId": "usuario-demo",
              "Estado": "{{estado}}",
              "FechaCreacionUtc": "2026-11-01T20:00:00Z",
              "FechaVencimientoUtc": "2026-11-01T20:05:00Z",
              "FechaEventoUtc": "2026-11-01T20:00:01Z",
              "TraceId": "{{traceId}}"
            }
            """;

        return new IncomingEvent(
            MessageId: null,
            Subject: tipo,
            CorrelationId: SolicitudId,
            Body: body,
            Properties: new Dictionary<string, string>
            {
                ["TipoEvento"] = tipo,
                ["TraceId"] = traceId,
                ["SolicitudId"] = SolicitudId,
            });
    }
}
