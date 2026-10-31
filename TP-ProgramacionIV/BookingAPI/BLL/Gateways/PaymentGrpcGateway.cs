using BookingAPI.BLL.DTOs;
using BookingAPI.BLL.Interfaces;
using BookingAPI.BLL.Protos;
using BookingAPI.DAL.Entities;
using Grpc.Core;
using Polly;
using Polly.Timeout;
using Polly.Wrap;

namespace BookingAPI.BLL.Gateways
{
    // [JUSTIFICACIÓN TEÓRICA - N-LAYER & GATEWAY PATTERN]
    // Esta clase pertenece a la capa BLL (Business Logic Layer) y actúa como un Gateway de integración
    // hacia el microservicio externo Payment gRPC API. Desacopla la lógica de negocio pura de Booking
    // de los detalles de red, serialización Protobuf y transporte HTTP/2 de gRPC.
    //
    // [JUSTIFICACIÓN TEÓRICA - RESILIENCIA CON POLLY & DEGRADACIÓN ELEGANTE (PUNTO 3.j)]
    // En arquitecturas de microservicios, las llamadas de red son inherentemente falibles.
    // Se implementa una política compuesta de resiliencia mediante Polly:
    // 1. Timeout: Evita retener indefinidamente hilos y recursos si el microservicio de pagos no responde.
    // 2. Reintentos (Wait and Retry): Mitiga fallos transitorios de red con espera exponencial antes de desistir.
    // 3. Degradación Elegante: Si Payment está caído o agota los reintentos, se captura la excepción
    //    y se retorna ServicioCaido = true, permitiendo que la reserva pase a estado 'Rechazada' con el
    //    motivo 'PagoNoDisponible', preservando la estabilidad del sistema sin arrojar HTTP 500 no controlado.
    public class PaymentGrpcGateway : IPaymentGateway
    {
        private readonly PaymentService.PaymentServiceClient _paymentClient;
        private readonly ILogger<PaymentGrpcGateway> _logger;
        private readonly AsyncPolicyWrap _resiliencePolicy;

        public PaymentGrpcGateway(
            PaymentService.PaymentServiceClient paymentClient,
            ILogger<PaymentGrpcGateway> logger)
        {
            _paymentClient = paymentClient;
            _logger = logger;

            // Define el tiempo maximo de espera (3 segundos) por intento para no bloquear hilos del servidor
            var timeoutPolicy = Policy.TimeoutAsync(3, TimeoutStrategy.Pessimistic, onTimeoutAsync: (context, timespan, task) =>
            {
                _logger.LogWarning("[RESILIENCIA - POLLY] Se superó el tiempo de espera ({Timeout}s) comunicando con Payment gRPC API.", timespan.TotalSeconds);
                return Task.CompletedTask;
            });

            // Configura 2 reintentos con backoff exponencial (200ms, 400ms) ante errores transitorios de red o servidor no disponible
            var retryPolicy = Policy
                .Handle<RpcException>(ex => EsErrorTransitorio(ex.StatusCode))
                .WaitAndRetryAsync(
                    retryCount: 2,
                    sleepDurationProvider: retryAttempt => TimeSpan.FromMilliseconds(200 * Math.Pow(2, retryAttempt)),
                    onRetry: (exception, timespan, retryCount, context) =>
                    {
                        _logger.LogWarning("[RESILIENCIA - POLLY] Reintento {RetryCount} al llamar a Payment gRPC tras {Delay}ms. Causa: {Message}",
                            retryCount, timespan.TotalMilliseconds, exception.Message);
                    });

            // Envuelve el timeout dentro de los reintentos: cada intento individual tiene su propio timeout de 3s
            _resiliencePolicy = Policy.WrapAsync(retryPolicy, timeoutPolicy);
        }

        public async Task<ResultadoCobroDto> CobrarReservaAsync(Reserva reserva, decimal monto)
        {
            // [DUPLA 4 - LOG DE AUDITORÍA / TRACEID]: Genera identificador unico para correlacionar la llamada entre microservicios
            string traceId = Guid.NewGuid().ToString("N");

            _logger.LogInformation(
                "// [DUPLA 4 - LOG DE AUDITORÍA / TRACEID]: Iniciando invocación gRPC a Payment API. TraceId: {TraceId}, SolicitudId: {SolicitudId}, ReservaId: {ReservaId}, Monto: {Monto}",
                traceId, reserva.SolicitudId, reserva.ReservaId, monto);

            // Mapea la entidad de dominio Reserva hacia el mensaje Protobuf fuertemente tipado
            var request = new ProcesarPagoRequest
            {
                ReservaId = reserva.ReservaId.ToString(),
                SolicitudId = reserva.SolicitudId.ToString(),
                UsuarioId = reserva.UsuarioId,
                Monto = (double)monto,
                TraceId = traceId
            };

            // Propaga el TraceId en los metadatos gRPC (headers HTTP/2) para mantener la cadena de trazabilidad
            var headers = new Metadata
            {
                { "x-trace-id", traceId },
                { "x-solicitud-id", reserva.SolicitudId.ToString() }
            };

            try
            {
                // Ejecuta la llamada gRPC bajo la proteccion combinada de las politicas de Polly (Timeout + Reintentos)
                ProcesarPagoResponse response = await _resiliencePolicy.ExecuteAsync(async () =>
                {
                    return await _paymentClient.ProcesarPagoAsync(
                        request,
                        headers: headers,
                        deadline: DateTime.UtcNow.AddSeconds(5));
                });

                // Evalua si la pasarela de pagos aprobo el cobro del monto de la reserva
                if (response.Exitoso)
                {
                    _logger.LogInformation(
                        "// [DUPLA 4 - LOG DE AUDITORÍA / TRACEID]: Cobro procesado exitosamente por Payment API. TraceId: {TraceId}, TransaccionId: {TransaccionId}",
                        traceId, response.TransaccionId);

                    return new ResultadoCobroDto
                    {
                        Exitoso = true,
                        ServicioCaido = false,
                        TransaccionId = response.TransaccionId,
                        ErrorMensaje = null
                    };
                }
                else
                {
                    // Cobro rechazado por motivo de negocio (fondos insuficientes, tarjeta expirada, etc.)
                    _logger.LogWarning(
                        "// [DUPLA 4 - LOG DE AUDITORÍA / TRACEID]: Cobro rechazado por motivo de negocio en Payment API. TraceId: {TraceId}, Detalle: {MensajeError}",
                        traceId, response.MensajeError);

                    return new ResultadoCobroDto
                    {
                        Exitoso = false,
                        ServicioCaido = false,
                        TransaccionId = response.TransaccionId,
                        ErrorMensaje = response.MensajeError
                    };
                }
            }
            catch (TimeoutRejectedException ex)
            {
                // [JUSTIFICACIÓN TEÓRICA - DEGRADACIÓN ELEGANTE ANTE TIMEOUT]
                // Si Payment no responde en el tiempo limite, se degrada a ServicioCaido para rechazar con motivo 'PagoNoDisponible'
                _logger.LogError(
                    ex,
                    "// [DUPLA 4 - LOG DE AUDITORÍA / TRACEID]: Timeout comunicando con Payment gRPC API. Degradando elegantemente a PagoNoDisponible. TraceId: {TraceId}, SolicitudId: {SolicitudId}",
                    traceId, reserva.SolicitudId);

                return new ResultadoCobroDto
                {
                    Exitoso = false,
                    ServicioCaido = true,
                    TransaccionId = null,
                    ErrorMensaje = "Tiempo de espera agotado al conectar con el microservicio de pagos (Timeout)."
                };
            }
            catch (RpcException ex)
            {
                // [JUSTIFICACIÓN TEÓRICA - DEGRADACIÓN ELEGANTE ANTE CAÍDA DEL SERVICIO]
                // Si el microservicio esta caido o inalcanzable, se previene propagar un HTTP 500 no controlado
                _logger.LogError(
                    ex,
                    "// [DUPLA 4 - LOG DE AUDITORÍA / TRACEID]: Falla RPC comunicando con Payment gRPC API (Status: {StatusCode}). TraceId: {TraceId}, SolicitudId: {SolicitudId}",
                    ex.StatusCode, traceId, reserva.SolicitudId);

                return new ResultadoCobroDto
                {
                    Exitoso = false,
                    ServicioCaido = true,
                    TransaccionId = null,
                    ErrorMensaje = $"Error de comunicación con Payment gRPC API: {ex.Status.Detail}"
                };
            }
            catch (Exception ex)
            {
                // Captura de resguardo ante cualquier otro fallo de conexion o serializacion
                _logger.LogError(
                    ex,
                    "// [DUPLA 4 - LOG DE AUDITORÍA / TRACEID]: Excepción inesperada en invocación a Payment. Degradando elegantemente. TraceId: {TraceId}, SolicitudId: {SolicitudId}",
                    traceId, reserva.SolicitudId);

                return new ResultadoCobroDto
                {
                    Exitoso = false,
                    ServicioCaido = true,
                    TransaccionId = null,
                    ErrorMensaje = $"Error inesperado al conectar con el servicio de pagos: {ex.Message}"
                };
            }
        }

        // Filtra los codigos gRPC que representan fallas de infraestructura o temporales que justifican un reintento
        private static bool EsErrorTransitorio(StatusCode statusCode)
        {
            return statusCode switch
            {
                StatusCode.Unavailable => true,
                StatusCode.DeadlineExceeded => true,
                StatusCode.ResourceExhausted => true,
                StatusCode.Internal => true,
                StatusCode.Aborted => true,
                _ => false
            };
        }
    }
}
