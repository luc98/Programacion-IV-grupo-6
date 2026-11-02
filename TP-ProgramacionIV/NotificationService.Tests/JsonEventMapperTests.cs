using Microsoft.Extensions.Options;
using NotificationService.Core;
using NotificationService.Mapping;
using NotificationService.Messaging;

namespace NotificationService.Tests;

public class JsonEventMapperTests
{
    private static JsonEventMapper MapperWith(string recipientField = "UsuarioId", params string[] deduplicationFields) =>
        new(Options.Create(new MappingOptions
        {
            RecipientField = recipientField,
            DeduplicationFields = [.. deduplicationFields],
        }));

    private static IncomingEvent Event(
        string body,
        string? subject = "ReservaCreada",
        string? messageId = null,
        string? correlationId = "solicitud-1",
        Dictionary<string, string>? properties = null) =>
        new(messageId, subject, correlationId, body, properties ?? new Dictionary<string, string>());

    private static NotificationRequest Map(JsonEventMapper mapper, IncomingEvent incoming)
    {
        Assert.True(mapper.TryMap(incoming, out var request, out var error), error);
        return request!;
    }

    [Fact]
    public void Traduce_el_mensaje_al_modelo_interno()
    {
        var request = Map(MapperWith(), Event("""{ "UsuarioId": "ana", "ReservaId": "r-1" }"""));

        Assert.Equal("ana", request.RecipientId);
        Assert.Equal("ReservaCreada", request.Type);
        Assert.Equal("solicitud-1", request.CorrelationId);
        Assert.Equal("r-1", request.Data["reservaid"]);
    }

    [Fact]
    public void El_campo_del_destinatario_sale_de_la_configuracion()
    {
        var request = Map(MapperWith(recipientField: "to"), Event("""{ "to": "luis" }"""));

        Assert.Equal("luis", request.RecipientId);
    }

    [Fact]
    public void Los_valores_que_no_son_texto_pasan_como_texto()
    {
        var request = Map(MapperWith(), Event("""{ "UsuarioId": "ana", "Monto": 12.5, "Pagado": true, "Nada": null }"""));

        Assert.Equal("12.5", request.Data["Monto"]);
        Assert.Equal("true", request.Data["Pagado"]);
        Assert.DoesNotContain("Nada", request.Data.Keys);
    }

    [Fact]
    public void Suma_las_propiedades_del_mensaje_sin_pisar_los_datos_del_cuerpo()
    {
        var properties = new Dictionary<string, string> { ["TraceId"] = "t-1", ["UsuarioId"] = "otro" };

        var request = Map(MapperWith(), Event("""{ "UsuarioId": "ana" }""", properties: properties));

        Assert.Equal("t-1", request.Data["TraceId"]);
        Assert.Equal("ana", request.RecipientId);
    }

    [Theory]
    [InlineData("esto no es un JSON")]
    [InlineData("")]
    public void Un_cuerpo_que_no_es_JSON_no_se_puede_traducir(string body)
    {
        Assert.False(MapperWith().TryMap(Event(body), out var request, out var error));
        Assert.Null(request);
        Assert.Contains("JSON", error);
    }

    [Fact]
    public void Un_JSON_que_no_es_un_objeto_no_se_puede_traducir()
    {
        Assert.False(MapperWith().TryMap(Event("[1, 2]"), out _, out var error));
        Assert.Contains("no es un objeto", error);
    }

    [Fact]
    public void Sin_destinatario_no_se_puede_traducir_y_el_motivo_nombra_el_campo()
    {
        Assert.False(MapperWith().TryMap(Event("""{ "ReservaId": "r-1" }"""), out _, out var error));
        Assert.Contains("UsuarioId", error);
    }

    [Fact]
    public void Sin_Subject_no_se_puede_traducir()
    {
        Assert.False(MapperWith().TryMap(Event("""{ "UsuarioId": "ana" }""", subject: null), out _, out var error));
        Assert.Contains("Subject", error);
    }

    [Fact]
    public void La_clave_de_duplicados_usa_el_tipo_y_los_campos_de_negocio()
    {
        var mapper = MapperWith(deduplicationFields: "ReservaId");

        var primero = Map(mapper, Event("""{ "UsuarioId": "ana", "ReservaId": "r-1", "TraceId": "a" }"""));
        var segundo = Map(mapper, Event("""{ "UsuarioId": "ana", "ReservaId": "r-1", "TraceId": "b" }"""));
        var otroTipo = Map(mapper, Event("""{ "UsuarioId": "ana", "ReservaId": "r-1" }""", subject: "ReservaConfirmada"));

        Assert.Equal("ReservaCreada|r-1", primero.DeduplicationKey);
        Assert.Equal(primero.DeduplicationKey, segundo.DeduplicationKey);
        Assert.NotEqual(primero.DeduplicationKey, otroTipo.DeduplicationKey);
    }

    [Fact]
    public void Si_falta_un_campo_de_negocio_usa_el_MessageId()
    {
        var mapper = MapperWith(deduplicationFields: "ReservaId");

        var request = Map(mapper, Event("""{ "UsuarioId": "ana" }""", messageId: "m-9"));

        Assert.Equal("id:m-9", request.DeduplicationKey);
    }

    [Fact]
    public void Sin_campos_de_negocio_ni_MessageId_usa_la_huella_del_cuerpo()
    {
        var mapper = MapperWith();
        const string body = """{ "UsuarioId": "ana" }""";

        var uno = Map(mapper, Event(body));
        var igual = Map(mapper, Event(body));
        var distinto = Map(mapper, Event("""{ "UsuarioId": "luis" }"""));

        Assert.StartsWith("body:", uno.DeduplicationKey);
        Assert.Equal(uno.DeduplicationKey, igual.DeduplicationKey);
        Assert.NotEqual(uno.DeduplicationKey, distinto.DeduplicationKey);
    }
}
