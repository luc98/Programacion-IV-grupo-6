using Microsoft.Extensions.Logging.Abstractions;
using PaymentAPI.BLL.DTOs;
using PaymentAPI.BLL.Exceptions;
using PaymentAPI.BLL.Services;
using PaymentAPI.DAL.Enums;
using PaymentAPI.DAL.Repositories;
using PaymentAPI.Tests.Fakes;

namespace PaymentAPI.Tests;

public class PagoServiceTests
{
    private static PagoService Servicio(IPagoRepository repositorio, BancoFalso banco) =>
        new(repositorio, banco, TimeProvider.System, NullLogger<PagoService>.Instance);

    private static SolicitudPagoDto Solicitud(Guid? reservaId = null, long monto = 1_500) =>
        new(reservaId ?? Guid.NewGuid(), Guid.NewGuid(), "ana", monto, "trace-123");

    [Fact]
    public async Task PrimerCobro_ApruebaYQuedaEnElHistorial()
    {
        var repositorio = new RepositorioEnMemoria();
        var solicitud = Solicitud();

        var resultado = await Servicio(repositorio, new BancoFalso()).ProcesarAsync(solicitud, CancellationToken.None);

        Assert.Equal(ResultadoPago.Aprobado, resultado.Resultado);
        Assert.False(resultado.EraRepetido);
        var pago = Assert.Single(repositorio.Pagos);
        Assert.Equal(resultado.PagoId, pago.PagoId);
        Assert.Equal(solicitud.ReservaId, pago.ReservaId);
        Assert.Equal(1_500, pago.MontoCentavos);
        Assert.Equal("trace-123", pago.TraceId);
    }

    [Fact]
    public async Task CobroRepetido_DevuelveElMismoPagoSinVolverAlBanco()
    {
        var repositorio = new RepositorioEnMemoria();
        var banco = new BancoFalso();
        var servicio = Servicio(repositorio, banco);
        var solicitud = Solicitud();

        var primero = await servicio.ProcesarAsync(solicitud, CancellationToken.None);
        var segundo = await servicio.ProcesarAsync(solicitud, CancellationToken.None);

        Assert.Equal(primero.PagoId, segundo.PagoId);
        Assert.True(segundo.EraRepetido);
        Assert.Equal(1, banco.Llamadas);
        Assert.Single(repositorio.Pagos);
    }

    [Fact]
    public async Task RechazoDelBanco_SeGuardaYUnReintentoDevuelveElMismoRechazo()
    {
        var repositorio = new RepositorioEnMemoria();
        var banco = new BancoFalso(DecisionBancoDto.FondosInsuficientes("sin fondos"));
        var servicio = Servicio(repositorio, banco);
        var solicitud = Solicitud();

        var primero = await servicio.ProcesarAsync(solicitud, CancellationToken.None);
        var segundo = await servicio.ProcesarAsync(solicitud, CancellationToken.None);

        Assert.Equal(ResultadoPago.RechazadoFondosInsuficientes, primero.Resultado);
        Assert.Equal("sin fondos", primero.Motivo);
        Assert.Equal(primero.PagoId, segundo.PagoId);
        Assert.Equal(1, banco.Llamadas);
    }

    [Fact]
    public async Task MismaReservaConOtroMonto_EsUnConflicto()
    {
        var repositorio = new RepositorioEnMemoria();
        var servicio = Servicio(repositorio, new BancoFalso());
        var reservaId = Guid.NewGuid();
        await servicio.ProcesarAsync(Solicitud(reservaId, monto: 1_500), CancellationToken.None);

        await Assert.ThrowsAsync<PagoEnConflictoException>(
            () => servicio.ProcesarAsync(Solicitud(reservaId, monto: 9_999), CancellationToken.None));
        Assert.Single(repositorio.Pagos);
    }

    [Fact]
    public async Task Carrera_DevuelveElPagoQueSeGuardoPrimero()
    {
        var repositorio = new RepositorioConCarrera();

        var resultado = await Servicio(repositorio, new BancoFalso()).ProcesarAsync(Solicitud(), CancellationToken.None);

        Assert.NotNull(repositorio.Ganador);
        Assert.Equal(repositorio.Ganador.PagoId, resultado.PagoId);
        Assert.True(resultado.EraRepetido);
        Assert.Single(repositorio.Pagos);
    }

    [Fact]
    public async Task SiElClienteCancelaMientrasDecideElBanco_NoSeGuardaNada()
    {
        var repositorio = new RepositorioEnMemoria();
        var banco = new BancoFalso { AntesDeDecidir = ct => Task.Delay(Timeout.Infinite, ct) };
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Servicio(repositorio, banco).ProcesarAsync(Solicitud(), cts.Token));
        Assert.Empty(repositorio.Pagos);
    }
}
