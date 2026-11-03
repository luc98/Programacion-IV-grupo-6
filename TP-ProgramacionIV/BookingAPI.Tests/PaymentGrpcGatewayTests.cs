using System.Diagnostics;
using BookingAPI.BLL.Gateways;
using BookingAPI.BLL.Options;
using BookingAPI.DAL.Entities;
using BookingAPI.Tests.Fakes;
using Grpc.Core;
using Grupo6.Payment.Contracts.V1;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BookingAPI.Tests;

// El gateway con el pipeline de Polly real, contra un Payment simulado. Los tiempos se achican
// (intentos de 200 ms, corte del circuito de 0,5 s) para que los tests corran rápido; la lógica
// es la misma que con los valores de producción.
public class PaymentGrpcGatewayTests
{
    private static readonly ResilienciaPagoOptions OpcionesRapidas = new()
    {
        TimeoutTotalSegundos = 3,
        TimeoutPorIntentoSegundos = 0.2,
        Reintentos = 2,
        DemoraInicialMs = 1,
        DemoraMaximaMs = 5,
        ProporcionDeFallas = 0.5,
        MinimoDeLlamadas = 3,
        VentanaSegundos = 30,
        CorteSegundos = 0.5
    };

    private readonly PaymentFalso _payment = new();
    private readonly TokenProviderFalso _tokens = new();
    private readonly PaymentGrpcGateway _gateway;

    public PaymentGrpcGatewayTests()
    {
        // Un pipeline nuevo por test: cada uno arranca con el circuito cerrado
        var resiliencia = new ResilienciaPago(Options.Create(OpcionesRapidas), NullLogger<ResilienciaPago>.Instance);
        _gateway = new PaymentGrpcGateway(_payment.Cliente, resiliencia, _tokens, NullLogger<PaymentGrpcGateway>.Instance);
    }

    private static Reserva NuevaReserva() => new()
    {
        ReservaId = Guid.NewGuid(),
        SolicitudId = Guid.NewGuid(),
        UsuarioId = "ana"
    };

    [Fact]
    public async Task CobroAprobado_EsExitoso_YMandaTokenTraceIdYCentavos()
    {
        var reserva = NuevaReserva();

        var resultado = await _gateway.CobrarReservaAsync(reserva, 1234.56m);

        Assert.True(resultado.Exitoso);
        Assert.False(resultado.ServicioCaido);
        Assert.False(string.IsNullOrEmpty(resultado.TransaccionId));
        var (pedido, cabeceras) = Assert.Single(_payment.Recibidas);
        Assert.Equal(123456, pedido.MontoCentavos);
        Assert.Equal(reserva.ReservaId.ToString(), pedido.ReservaId);
        Assert.Equal(reserva.SolicitudId.ToString(), pedido.SolicitudId);
        Assert.Equal("ana", pedido.UsuarioId);
        Assert.Equal($"Bearer {TokenProviderFalso.Token}", cabeceras.GetValue("authorization"));
        Assert.Equal(reserva.SolicitudId.ToString(), cabeceras.GetValue("x-trace-id"));
    }

    [Fact]
    public async Task FondosInsuficientes_EsPagoFallido_YNoSeReintenta()
    {
        _payment.Luego(PaymentFalso.RechazaPorFondos());

        var resultado = await _gateway.CobrarReservaAsync(NuevaReserva(), 100m);

        Assert.False(resultado.Exitoso);
        Assert.False(resultado.ServicioCaido);
        Assert.Equal("sin fondos", resultado.ErrorMensaje);
        Assert.Equal(1, _payment.Llamadas);
    }

    [Fact]
    public async Task UnavailableDosVecesYDespuesResponde_ReintentaYAprueba()
    {
        _payment.Luego(PaymentFalso.Falla(StatusCode.Unavailable), veces: 2).Luego(PaymentFalso.Aprueba());

        var resultado = await _gateway.CobrarReservaAsync(NuevaReserva(), 100m);

        Assert.True(resultado.Exitoso);
        Assert.Equal(3, _payment.Llamadas);
    }

    [Fact]
    public async Task ReintentosConLaMismaReserva_MandanElMismoReservaId()
    {
        _payment.Luego(PaymentFalso.Falla(StatusCode.Unavailable)).Luego(PaymentFalso.Aprueba());
        var reserva = NuevaReserva();

        await _gateway.CobrarReservaAsync(reserva, 100m);

        // Es lo que permite a Payment reconocer el reintento y no cobrar dos veces
        Assert.All(_payment.Recibidas, r => Assert.Equal(reserva.ReservaId.ToString(), r.Pedido.ReservaId));
    }

    [Fact]
    public async Task PaymentCaido_AgotaLosReintentos_PagoNoDisponible()
    {
        _payment.Luego(PaymentFalso.Falla(StatusCode.Unavailable), veces: 3);

        var resultado = await _gateway.CobrarReservaAsync(NuevaReserva(), 100m);

        Assert.False(resultado.Exitoso);
        Assert.True(resultado.ServicioCaido);
        Assert.Equal(3, _payment.Llamadas);
    }

    [Theory]
    [InlineData(StatusCode.InvalidArgument)]
    [InlineData(StatusCode.FailedPrecondition)]
    [InlineData(StatusCode.Internal)]
    [InlineData(StatusCode.DeadlineExceeded)]
    [InlineData(StatusCode.PermissionDenied)]
    public async Task ErrorNoTransitorio_NoSeReintenta_PagoNoDisponible(StatusCode status)
    {
        _payment.Luego(PaymentFalso.Falla(status));

        var resultado = await _gateway.CobrarReservaAsync(NuevaReserva(), 100m);

        Assert.True(resultado.ServicioCaido);
        Assert.Equal(1, _payment.Llamadas);
    }

    [Fact]
    public async Task IntentoColgado_SeCortaPorTimeout_YSeReintenta()
    {
        _payment.Luego(PaymentFalso.Cuelga(), veces: 3);
        var cronometro = Stopwatch.StartNew();

        var resultado = await _gateway.CobrarReservaAsync(NuevaReserva(), 100m);

        Assert.True(resultado.ServicioCaido);
        Assert.Equal(3, _payment.Llamadas);
        // Tres intentos de 200 ms, muy por debajo del timeout total de 3 s
        Assert.True(cronometro.Elapsed < TimeSpan.FromSeconds(2), $"Tardó {cronometro.Elapsed}");
    }

    [Fact]
    public async Task IntentoColgadoYDespuesResponde_Aprueba()
    {
        _payment.Luego(PaymentFalso.Cuelga()).Luego(PaymentFalso.Aprueba());

        var resultado = await _gateway.CobrarReservaAsync(NuevaReserva(), 100m);

        Assert.True(resultado.Exitoso);
        Assert.Equal(2, _payment.Llamadas);
    }

    [Fact]
    public async Task ConElCircuitoAbierto_SeRechazaSinLlamarAPayment()
    {
        // Primer cobro: 3 intentos fallidos, que alcanzan el mínimo de llamadas y abren el circuito
        _payment.Luego(PaymentFalso.Falla(StatusCode.Unavailable), veces: 3);
        await _gateway.CobrarReservaAsync(NuevaReserva(), 100m);
        Assert.Equal(3, _payment.Llamadas);

        var cronometro = Stopwatch.StartNew();
        var resultado = await _gateway.CobrarReservaAsync(NuevaReserva(), 100m);

        Assert.True(resultado.ServicioCaido);
        Assert.Contains("circuito", resultado.ErrorMensaje);
        Assert.Equal(3, _payment.Llamadas);
        Assert.True(cronometro.Elapsed < TimeSpan.FromMilliseconds(100), $"Tardó {cronometro.Elapsed}");
    }

    [Fact]
    public async Task PasadoElCorte_ElCircuitoPruebaDeNuevo_YSiPaymentRespondeSeCierra()
    {
        _payment.Luego(PaymentFalso.Falla(StatusCode.Unavailable), veces: 3);
        await _gateway.CobrarReservaAsync(NuevaReserva(), 100m);

        await Task.Delay(TimeSpan.FromSeconds(OpcionesRapidas.CorteSegundos + 0.2));
        var sonda = await _gateway.CobrarReservaAsync(NuevaReserva(), 100m);
        var siguiente = await _gateway.CobrarReservaAsync(NuevaReserva(), 100m);

        Assert.True(sonda.Exitoso);
        Assert.True(siguiente.Exitoso);
        Assert.Equal(5, _payment.Llamadas);
    }

    [Fact]
    public async Task LosRechazosDeNegocio_NoAbrenElCircuito()
    {
        _payment.Luego(PaymentFalso.RechazaPorFondos(), veces: 5);
        for (var i = 0; i < 5; i++)
        {
            await _gateway.CobrarReservaAsync(NuevaReserva(), 100m);
        }

        var resultado = await _gateway.CobrarReservaAsync(NuevaReserva(), 100m);

        Assert.True(resultado.Exitoso);
        Assert.Equal(6, _payment.Llamadas);
    }

    [Fact]
    public async Task Unauthenticated_DescartaElToken_YNoSeReintenta()
    {
        _payment.Luego(PaymentFalso.Falla(StatusCode.Unauthenticated));

        var resultado = await _gateway.CobrarReservaAsync(NuevaReserva(), 100m);

        Assert.True(resultado.ServicioCaido);
        Assert.Equal(1, _tokens.Invalidaciones);
        Assert.Equal(1, _payment.Llamadas);
    }

    [Fact]
    public async Task SinTokenDeKeycloak_NoSeLlamaAPayment_PagoNoDisponible()
    {
        _tokens.Falla = true;

        var resultado = await _gateway.CobrarReservaAsync(NuevaReserva(), 100m);

        Assert.True(resultado.ServicioCaido);
        Assert.Equal(0, _payment.Llamadas);
    }

    [Fact]
    public async Task ResultadoQueElContratoNoDefine_PagoNoDisponible()
    {
        _payment.Luego(PaymentFalso.Responde(new ProcesarPagoResponse { Resultado = ResultadoPago.Unspecified }));

        var resultado = await _gateway.CobrarReservaAsync(NuevaReserva(), 100m);

        Assert.False(resultado.Exitoso);
        Assert.True(resultado.ServicioCaido);
    }
}
