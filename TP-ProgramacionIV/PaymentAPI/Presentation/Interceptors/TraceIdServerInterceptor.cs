using System.Diagnostics;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Serilog.Context;

namespace PaymentAPI.Presentation.Interceptors;

// Plantilla de logs de dupla 4 (punto 3.f). Corre alrededor de cada llamada gRPC:
// toma el TraceId que manda Booking en la metadata, lo pone en todos los logs de
// la llamada y registra el inicio y el fin con el resultado y la duración.
public sealed class TraceIdServerInterceptor(ILogger<TraceIdServerInterceptor> logger) : Interceptor
{
    public const string MetadataKey = "x-trace-id";
    public const string PropertyName = "TraceId";

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        // Quien recibe un TraceId lo reutiliza; solo se genera uno si no vino
        var traceId = context.RequestHeaders.GetValue(MetadataKey);
        if (string.IsNullOrWhiteSpace(traceId))
        {
            traceId = Guid.NewGuid().ToString();
        }
        context.UserState[PropertyName] = traceId;

        using (LogContext.PushProperty(PropertyName, traceId))
        {
            var operacion = context.Method;
            var cronometro = Stopwatch.StartNew();
            var resultado = StatusCode.OK.ToString();
            logger.LogInformation("{Etapa} {Operacion}", "Inicio", operacion);
            try
            {
                return await continuation(request, context);
            }
            catch (RpcException ex)
            {
                resultado = ex.StatusCode.ToString();
                throw;
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                // El cliente cortó (timeout de Polly o deadline): no es un error de Payment
                resultado = StatusCode.Cancelled.ToString();
                throw;
            }
            catch (Exception ex)
            {
                // Red de seguridad: se registra con el TraceId y se responde Internal
                // sin filtrar detalles internos al cliente.
                resultado = StatusCode.Internal.ToString();
                logger.LogError(ex, "Error no controlado en {Operacion}", operacion);
                throw new RpcException(new Status(StatusCode.Internal, "Error interno en Payment"));
            }
            finally
            {
                logger.LogInformation(
                    "{Etapa} {Operacion} Resultado={Resultado} DuracionMs={DuracionMs}",
                    "Fin", operacion, resultado, cronometro.ElapsedMilliseconds);
            }
        }
    }

    // Para que el servicio guarde en el pago el mismo TraceId que va en los logs
    public static string ObtenerTraceId(ServerCallContext context) =>
        context.UserState.TryGetValue(PropertyName, out var valor) && valor is string traceId
            ? traceId
            : string.Empty;
}
