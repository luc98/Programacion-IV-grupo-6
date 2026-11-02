using System.Data.Common;
using Grpc.Core;
using Grupo6.Payment.Contracts.V1;
using PaymentAPI.BLL.DTOs;
using PaymentAPI.BLL.Exceptions;
using PaymentAPI.BLL.Interfaces;
using PaymentAPI.Presentation.Interceptors;
using ResultadoPagoDal = PaymentAPI.DAL.Enums.ResultadoPago;

namespace PaymentAPI.Presentation.Services;

// Capa de presentación: traduce entre el contrato gRPC y la BLL, y decide qué
// status gRPC corresponde a cada error. No tiene reglas de negocio.
// Es internal porque la clase base que genera Grpc.Tools también lo es (ver csproj).
internal sealed class PaymentGrpcService(IPagoService pagoService, ILogger<PaymentGrpcService> logger)
    : PaymentService.PaymentServiceBase
{
    public override async Task<ProcesarPagoResponse> ProcesarPago(ProcesarPagoRequest request, ServerCallContext context)
    {
        var solicitud = Validar(request, TraceIdServerInterceptor.ObtenerTraceId(context));

        try
        {
            var resultado = await pagoService.ProcesarAsync(solicitud, context.CancellationToken);
            return new ProcesarPagoResponse
            {
                TransaccionId = resultado.PagoId.ToString(),
                Resultado = resultado.Resultado switch
                {
                    ResultadoPagoDal.Aprobado => ResultadoPago.Aprobado,
                    ResultadoPagoDal.RechazadoFondosInsuficientes => ResultadoPago.RechazadoFondosInsuficientes,
                    _ => ResultadoPago.Unspecified
                },
                MensajeError = resultado.Motivo ?? string.Empty
            };
        }
        catch (PagoEnConflictoException ex)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message));
        }
        catch (Exception ex) when (EsFallaTransitoriaDeLaBase(ex))
        {
            // Unavailable es el único status que Booking reintenta. Es seguro: si el
            // pago llegó a guardarse, el reintento lo encuentra por reserva_id.
            logger.LogError(ex, "La base de pagos no está disponible");
            throw new RpcException(new Status(StatusCode.Unavailable, "La base de pagos no está disponible"));
        }
    }

    // Validación de la entrada del contrato. Un pedido mal formado es un error del
    // cliente (InvalidArgument), que no tiene sentido reintentar.
    private static SolicitudPagoDto Validar(ProcesarPagoRequest request, string traceId)
    {
        if (!Guid.TryParse(request.ReservaId, out var reservaId))
        {
            throw Invalido("reserva_id debe ser un Guid");
        }
        if (!Guid.TryParse(request.SolicitudId, out var solicitudId))
        {
            throw Invalido("solicitud_id debe ser un Guid");
        }
        if (string.IsNullOrWhiteSpace(request.UsuarioId))
        {
            throw Invalido("usuario_id es obligatorio");
        }
        if (request.MontoCentavos <= 0)
        {
            throw Invalido("monto_centavos debe ser mayor a cero");
        }

        return new SolicitudPagoDto(reservaId, solicitudId, request.UsuarioId, request.MontoCentavos, traceId);
    }

    private static RpcException Invalido(string detalle) =>
        new(new Status(StatusCode.InvalidArgument, detalle));

    // DbException es la clase base de las excepciones de ADO.NET: sirve para
    // cualquier proveedor, sin que la presentación dependa de Npgsql. Se recorre toda
    // la cadena porque EF la envuelve: al guardar queda
    // InvalidOperationException -> DbUpdateException -> NpgsqlException.
    private static bool EsFallaTransitoriaDeLaBase(Exception ex)
    {
        if (ex is OperationCanceledException)
        {
            return false;
        }
        for (Exception? actual = ex; actual is not null; actual = actual.InnerException)
        {
            if (actual is DbException { IsTransient: true })
            {
                return true;
            }
        }
        return false;
    }
}
