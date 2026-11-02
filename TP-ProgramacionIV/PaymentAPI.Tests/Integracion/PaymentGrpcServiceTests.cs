using System.Data.Common;
using System.Text;
using Grpc.Core;
using Grupo6.Payment.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace PaymentAPI.Tests.Integracion;

public class PaymentGrpcServiceTests(PaymentApiFactory factory) : IClassFixture<PaymentApiFactory>
{
    private static ProcesarPagoRequest Pedido(long montoCentavos = 1_500, Guid? reservaId = null) => new()
    {
        ReservaId = (reservaId ?? Guid.NewGuid()).ToString(),
        SolicitudId = Guid.NewGuid().ToString(),
        UsuarioId = "ana",
        MontoCentavos = montoCentavos
    };

    private static Metadata Cabeceras(string? token, string? traceId = null)
    {
        var metadata = new Metadata();
        if (token is not null)
        {
            metadata.Add("authorization", $"Bearer {token}");
        }
        if (traceId is not null)
        {
            metadata.Add("x-trace-id", traceId);
        }
        return metadata;
    }

    private async Task<StatusCode> StatusDe(Metadata cabeceras, ProcesarPagoRequest? pedido = null)
    {
        var ex = await Assert.ThrowsAsync<RpcException>(
            () => factory.CrearCliente().ProcesarPagoAsync(pedido ?? Pedido(), cabeceras).ResponseAsync);
        return ex.StatusCode;
    }

    [Fact]
    public async Task SinToken_Unauthenticated()
    {
        Assert.Equal(StatusCode.Unauthenticated, await StatusDe(Cabeceras(token: null)));
    }

    [Fact]
    public async Task TokenDeOtroClienteDelRealm_PermissionDenied()
    {
        var token = PaymentApiFactory.CrearToken(azp: "notifications-api");

        Assert.Equal(StatusCode.PermissionDenied, await StatusDe(Cabeceras(token)));
    }

    [Fact]
    public async Task TokenSinAudienciaPayment_Unauthenticated()
    {
        var token = PaymentApiFactory.CrearToken(audiencia: "account");

        Assert.Equal(StatusCode.Unauthenticated, await StatusDe(Cabeceras(token)));
    }

    [Fact]
    public async Task TokenDeOtroEmisor_Unauthenticated()
    {
        var token = PaymentApiFactory.CrearToken(emisor: "http://otro-keycloak/realms/booking-realm");

        Assert.Equal(StatusCode.Unauthenticated, await StatusDe(Cabeceras(token)));
    }

    [Fact]
    public async Task TokenFirmadoConOtraClave_Unauthenticated()
    {
        var otraClave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("otra-clave-que-no-es-la-del-realm-de-prueba"));
        var token = PaymentApiFactory.CrearToken(clave: otraClave);

        Assert.Equal(StatusCode.Unauthenticated, await StatusDe(Cabeceras(token)));
    }

    [Fact]
    public async Task TokenVencido_Unauthenticated()
    {
        var token = PaymentApiFactory.CrearToken(vence: DateTime.UtcNow.AddMinutes(-10));

        Assert.Equal(StatusCode.Unauthenticated, await StatusDe(Cabeceras(token)));
    }

    [Fact]
    public async Task TokenValido_ApruebaYGuardaElTraceIdQueMandoBooking()
    {
        var pedido = Pedido();
        var traceId = Guid.NewGuid().ToString();

        var respuesta = await factory.CrearCliente().ProcesarPagoAsync(
            pedido, Cabeceras(PaymentApiFactory.CrearToken(), traceId));

        Assert.Equal(ResultadoPago.Aprobado, respuesta.Resultado);
        Assert.Equal(string.Empty, respuesta.MensajeError);
        var pago = Assert.Single(factory.Repositorio.Pagos, p => p.ReservaId == Guid.Parse(pedido.ReservaId));
        Assert.Equal(respuesta.TransaccionId, pago.PagoId.ToString());
        Assert.Equal(traceId, pago.TraceId);
    }

    [Fact]
    public async Task SinTraceId_GeneraUno()
    {
        var pedido = Pedido();

        await factory.CrearCliente().ProcesarPagoAsync(pedido, Cabeceras(PaymentApiFactory.CrearToken()));

        var pago = Assert.Single(factory.Repositorio.Pagos, p => p.ReservaId == Guid.Parse(pedido.ReservaId));
        Assert.False(string.IsNullOrWhiteSpace(pago.TraceId));
    }

    [Fact]
    public async Task FondosInsuficientes_EsUnaRespuestaOkConElRechazo()
    {
        var respuesta = await factory.CrearCliente().ProcesarPagoAsync(
            Pedido(montoCentavos: PaymentApiFactory.LimiteCentavos + 1), Cabeceras(PaymentApiFactory.CrearToken()));

        Assert.Equal(ResultadoPago.RechazadoFondosInsuficientes, respuesta.Resultado);
        Assert.NotEmpty(respuesta.MensajeError);
    }

    [Fact]
    public async Task MismaReservaDosVeces_DevuelveLaMismaTransaccion()
    {
        var pedido = Pedido();
        var cliente = factory.CrearCliente();
        var cabeceras = Cabeceras(PaymentApiFactory.CrearToken());

        var primera = await cliente.ProcesarPagoAsync(pedido, cabeceras);
        var segunda = await cliente.ProcesarPagoAsync(pedido, cabeceras);

        Assert.Equal(primera.TransaccionId, segunda.TransaccionId);
        Assert.Single(factory.Repositorio.Pagos, p => p.ReservaId == Guid.Parse(pedido.ReservaId));
    }

    [Fact]
    public async Task MismaReservaConOtroMonto_FailedPrecondition()
    {
        var reservaId = Guid.NewGuid();
        var cabeceras = Cabeceras(PaymentApiFactory.CrearToken());
        await factory.CrearCliente().ProcesarPagoAsync(Pedido(1_500, reservaId), cabeceras);

        Assert.Equal(StatusCode.FailedPrecondition, await StatusDe(cabeceras, Pedido(2_000, reservaId)));
    }

    [Theory]
    [InlineData("no-es-un-guid", 1_500)]
    [InlineData(null, 0)]
    [InlineData(null, -100)]
    public async Task PedidoMalFormado_InvalidArgument(string? reservaId, long montoCentavos)
    {
        var pedido = Pedido(montoCentavos);
        if (reservaId is not null)
        {
            pedido.ReservaId = reservaId;
        }

        Assert.Equal(StatusCode.InvalidArgument, await StatusDe(Cabeceras(PaymentApiFactory.CrearToken()), pedido));
    }
}

// Fallas técnicas: qué status gRPC recibe Booking, que es lo que decide si reintenta.
// Cada clase de test tiene su propia instancia de la factory, así la falla simulada
// no afecta a los otros tests.
public class PaymentBaseCaidaTests(PaymentApiFactory factory) : IClassFixture<PaymentApiFactory>
{
    private sealed class DbExceptionTransitoria() : DbException("conexión cortada")
    {
        public override bool IsTransient => true;
    }

    private static readonly Metadata Bearer = new() { { "authorization", $"Bearer {PaymentApiFactory.CrearToken()}" } };

    private static ProcesarPagoRequest Pedido() => new()
    {
        ReservaId = Guid.NewGuid().ToString(),
        SolicitudId = Guid.NewGuid().ToString(),
        UsuarioId = "ana",
        MontoCentavos = 1_500
    };

    private async Task<RpcException> Falla()
    {
        return await Assert.ThrowsAsync<RpcException>(
            () => factory.CrearCliente().ProcesarPagoAsync(Pedido(), Bearer).ResponseAsync);
    }

    [Fact]
    public async Task BaseCaidaAlGuardar_Unavailable_ParaQueBookingReintente()
    {
        // Así llega una falla transitoria de Npgsql durante SaveChanges: envuelta por EF dos veces
        factory.Repositorio.FallaAlAgregar = () => new InvalidOperationException(
            "An exception has been raised that is likely due to a transient failure.",
            new DbUpdateException("error al guardar", new DbExceptionTransitoria()));

        Assert.Equal(StatusCode.Unavailable, (await Falla()).StatusCode);
    }

    [Fact]
    public async Task ErrorInesperado_Internal_SinMostrarElDetalle()
    {
        factory.Repositorio.FallaAlAgregar = () => new InvalidOperationException("detalle interno que no debe salir");

        var ex = await Falla();

        Assert.Equal(StatusCode.Internal, ex.StatusCode);
        Assert.DoesNotContain("detalle interno", ex.Status.Detail);
    }
}

// El plan B de la decisión 14: sin el mapper de audiencia en Keycloak, Payment
// sigue exigiendo firma, emisor, vencimiento y que el token sea de Booking.
public class PaymentSinValidarAudienciaTests(PaymentApiSinAudienciaFactory factory)
    : IClassFixture<PaymentApiSinAudienciaFactory>
{
    private static Metadata Bearer(string token) => new() { { "authorization", $"Bearer {token}" } };

    private static ProcesarPagoRequest Pedido() => new()
    {
        ReservaId = Guid.NewGuid().ToString(),
        SolicitudId = Guid.NewGuid().ToString(),
        UsuarioId = "ana",
        MontoCentavos = 1_500
    };

    [Fact]
    public async Task TokenDeBookingSinAudiencia_Aprueba()
    {
        var token = PaymentApiFactory.CrearToken(audiencia: "account");

        var respuesta = await factory.CrearCliente().ProcesarPagoAsync(Pedido(), Bearer(token));

        Assert.Equal(ResultadoPago.Aprobado, respuesta.Resultado);
    }

    [Fact]
    public async Task TokenDeOtroCliente_SigueSiendoPermissionDenied()
    {
        var token = PaymentApiFactory.CrearToken(azp: "notifications-api", audiencia: "account");

        var ex = await Assert.ThrowsAsync<RpcException>(
            () => factory.CrearCliente().ProcesarPagoAsync(Pedido(), Bearer(token)).ResponseAsync);
        Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
    }
}
