using Microsoft.Extensions.Options;
using NotificationService.Core;
using NotificationService.Templates;

namespace NotificationService.Tests;

public class TemplateRendererTests
{
    private static TemplateRenderer RendererWith(params (string Type, string Template)[] templates)
    {
        var options = new NotificationOptions();
        foreach (var (type, template) in templates)
        {
            options.Templates[type] = template;
        }

        return new TemplateRenderer(Options.Create(options));
    }

    private static NotificationRequest Request(string type, params (string Key, string Value)[] data) =>
        new("usuario-demo", type, data.ToDictionary(d => d.Key, d => d.Value, StringComparer.OrdinalIgnoreCase), "clave");

    [Fact]
    public void Reemplaza_los_campos_de_la_plantilla_con_los_datos()
    {
        var renderer = RendererWith(("Creada", "Reserva {Id} hasta {Vence}."));

        var ok = renderer.TryRender(Request("Creada", ("Id", "42"), ("Vence", "20:05")), out var text);

        Assert.True(ok);
        Assert.Equal("Reserva 42 hasta 20:05.", text);
    }

    [Fact]
    public void Los_campos_no_distinguen_mayusculas()
    {
        var renderer = RendererWith(("Creada", "Hola {usuarioid}"));

        renderer.TryRender(Request("Creada", ("UsuarioId", "ana")), out var text);

        Assert.Equal("Hola ana", text);
    }

    [Fact]
    public void El_tipo_no_distingue_mayusculas()
    {
        var renderer = RendererWith(("ReservaCreada", "ok"));

        Assert.True(renderer.TryRender(Request("reservacreada"), out _));
    }

    [Fact]
    public void Si_falta_un_dato_lo_marca_en_lugar_de_dejar_las_llaves()
    {
        var renderer = RendererWith(("Creada", "Reserva {Id}"));

        renderer.TryRender(Request("Creada"), out var text);

        Assert.Equal("Reserva (sin dato)", text);
    }

    [Fact]
    public void Devuelve_false_si_no_hay_plantilla_para_el_tipo()
    {
        var renderer = RendererWith(("Creada", "ok"));

        var ok = renderer.TryRender(Request("Desconocido"), out var text);

        Assert.False(ok);
        Assert.Equal(string.Empty, text);
    }
}
