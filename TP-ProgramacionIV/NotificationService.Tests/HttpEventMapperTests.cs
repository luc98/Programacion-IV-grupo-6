using NotificationService.Http;

namespace NotificationService.Tests;

public class HttpEventMapperTests
{
    private const string Body = """{"tipoEvento":"ReservaCreada","traceId":"trace-del-cuerpo","usuarioId":"user-1"}""";

    [Fact]
    public void Los_headers_tienen_prioridad_sobre_el_cuerpo()
    {
        var incoming = HttpEventMapper.ToIncomingEvent("ReservaConfirmada", "trace-del-header", Body);

        Assert.Equal("ReservaConfirmada", incoming.Subject);
        Assert.Equal("trace-del-header", incoming.CorrelationId);
        Assert.Equal("trace-del-header", incoming.Properties["TraceId"]);
    }

    [Fact]
    public void Sin_headers_toma_el_tipo_y_la_traza_del_cuerpo()
    {
        var incoming = HttpEventMapper.ToIncomingEvent(null, null, Body);

        Assert.Equal("ReservaCreada", incoming.Subject);
        Assert.Equal("trace-del-cuerpo", incoming.CorrelationId);
        Assert.Equal("trace-del-cuerpo", incoming.Properties["TraceId"]);
    }

    [Fact]
    public void Los_headers_en_blanco_se_ignoran()
    {
        var incoming = HttpEventMapper.ToIncomingEvent("  ", "", Body);

        Assert.Equal("ReservaCreada", incoming.Subject);
        Assert.Equal("trace-del-cuerpo", incoming.CorrelationId);
    }

    [Fact]
    public void Los_nombres_del_cuerpo_no_distinguen_mayusculas()
    {
        var incoming = HttpEventMapper.ToIncomingEvent(null, null, """{"TipoEvento":"ReservaCreada","TRACEID":"t-1"}""");

        Assert.Equal("ReservaCreada", incoming.Subject);
        Assert.Equal("t-1", incoming.CorrelationId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("esto no es json")]
    [InlineData("[1,2,3]")]
    [InlineData("""{"tipoEvento":5}""")]
    public void Un_cuerpo_sin_tipo_ni_traza_utilizable_no_rompe_y_deja_el_tipo_vacio(string body)
    {
        var incoming = HttpEventMapper.ToIncomingEvent(null, null, body);

        Assert.Null(incoming.Subject);
        Assert.Null(incoming.CorrelationId);
        Assert.Empty(incoming.Properties);
        Assert.Equal(body, incoming.Body);
    }

    [Fact]
    public void El_mensaje_por_HTTP_no_trae_MessageId_y_cuenta_como_primera_entrega()
    {
        var incoming = HttpEventMapper.ToIncomingEvent("ReservaCreada", null, Body);

        Assert.Null(incoming.MessageId);
        Assert.Equal(1, incoming.DeliveryCount);
    }
}
