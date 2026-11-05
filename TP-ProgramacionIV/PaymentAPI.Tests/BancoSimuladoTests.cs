using PaymentAPI.BLL.DTOs;
using PaymentAPI.BLL.Options;
using PaymentAPI.BLL.Services;
using PaymentAPI.DAL.Enums;
using PaymentAPI.Tests.Fakes;

namespace PaymentAPI.Tests;

public class BancoSimuladoTests
{
    private static BancoSimulado Banco(long limite = 10_000, int retardoMs = 0, params string[] sinFondos) =>
        new(new OpcionesFijas<BancoSimuladoOptions>(new BancoSimuladoOptions
        {
            LimitePorCobroCentavos = limite,
            RetardoMs = retardoMs,
            UsuariosSinFondos = [.. sinFondos]
        }));

    private static SolicitudPagoDto Solicitud(long montoCentavos, string usuario = "ana") =>
        new(Guid.NewGuid(), Guid.NewGuid(), usuario, montoCentavos, "trace");

    [Fact]
    public async Task MontoHastaElLimite_Aprueba()
    {
        var decision = await Banco(limite: 10_000).AutorizarAsync(Solicitud(10_000), CancellationToken.None);

        Assert.Equal(ResultadoPago.Aprobado, decision.Resultado);
        Assert.Null(decision.Motivo);
    }

    [Fact]
    public async Task MontoMayorAlLimite_RechazaPorFondosInsuficientes()
    {
        var decision = await Banco(limite: 10_000).AutorizarAsync(Solicitud(10_001), CancellationToken.None);

        Assert.Equal(ResultadoPago.RechazadoFondosInsuficientes, decision.Resultado);
        Assert.Contains("límite", decision.Motivo);
    }

    [Fact]
    public async Task LimiteCero_NoLimitaElMonto()
    {
        var decision = await Banco(limite: 0).AutorizarAsync(Solicitud(long.MaxValue), CancellationToken.None);

        Assert.Equal(ResultadoPago.Aprobado, decision.Resultado);
    }

    [Fact]
    public async Task UsuarioSinFondos_Rechaza_SinImportarMayusculas()
    {
        var decision = await Banco(10_000, 0, "usuario-sin-fondos")
            .AutorizarAsync(Solicitud(100, "Usuario-Sin-Fondos"), CancellationToken.None);

        Assert.Equal(ResultadoPago.RechazadoFondosInsuficientes, decision.Resultado);
    }

    [Fact]
    public async Task Retardo_SeCortaSiElClienteCancela()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Banco(retardoMs: 10_000).AutorizarAsync(Solicitud(100), cts.Token));
    }
}
