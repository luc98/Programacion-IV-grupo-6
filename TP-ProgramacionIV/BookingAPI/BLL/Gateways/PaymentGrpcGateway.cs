using System.Diagnostics;
using BookingAPI.BLL.DTOs;
using BookingAPI.BLL.Interfaces;
using BookingAPI.DAL.Entities;
using Grpc.Core;
using Grupo6.Payment.Contracts.V1;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace BookingAPI.BLL.Gateways
{
    // [JUSTIFICACIÓN TEÓRICA - N-LAYER & GATEWAY PATTERN]
    // Esta clase pertenece a la capa BLL y actúa como Gateway hacia Payment gRPC API. Desacopla la
    // lógica de negocio de Booking de los detalles de red, Protobuf, HTTP/2 y autenticación.
    //
    // [JUSTIFICACIÓN TEÓRICA - DEGRADACIÓN ELEGANTE (PUNTO 3.j)]
    // El gateway nunca lanza excepciones: siempre devuelve un ResultadoCobroDto. BookingService lo
    // traduce así:
    //   - Exitoso                  -> la reserva queda Confirmada
    //   - !Exitoso y !ServicioCaido -> Rechazada por PagoFallido (Payment respondió que no hay fondos)
    //   - ServicioCaido            -> Rechazada por PagoNoDisponible (Payment caído, lento, circuito
    //                                 abierto o cualquier falla técnica)
    // Así el mensaje de Service Bus se completa igual y Booking sigue funcionando aunque Payment no.
    public class PaymentGrpcGateway : IPaymentGateway
    {
        private const string Operacion = "CobrarReserva";
        private static readonly TimeSpan EsperaMaximaDelToken = TimeSpan.FromSeconds(5);

        private readonly PaymentService.PaymentServiceClient _paymentClient;
        private readonly ResiliencePipeline _pipeline;
        private readonly ITokenProvider _tokenProvider;
        private readonly ILogger<PaymentGrpcGateway> _logger;

        public PaymentGrpcGateway(
            PaymentService.PaymentServiceClient paymentClient,
            ResilienciaPago resiliencia,
            ITokenProvider tokenProvider,
            ILogger<PaymentGrpcGateway> logger)
        {
            _paymentClient = paymentClient;
            _pipeline = resiliencia.Pipeline;
            _tokenProvider = tokenProvider;
            _logger = logger;
        }

        public async Task<ResultadoCobroDto> CobrarReservaAsync(Reserva reserva, decimal monto)
        {
            // [DUPLA 4 - TRACEID]: el TraceId de la cadena es el SolicitudId que generó Availability.
            // Se reutiliza en lugar de crear uno nuevo, así Payment loguea el mismo valor.
            string traceId = reserva.SolicitudId.ToString();
            var cronometro = Stopwatch.StartNew();
            _logger.LogInformation(
                "{Etapa} {Operacion} TraceId={TraceId} ReservaId={ReservaId} Monto={Monto}",
                "Inicio", Operacion, traceId, reserva.ReservaId, monto);

            ResultadoCobroDto resultado;
            try
            {
                resultado = await CobrarAsync(reserva, monto, traceId);
            }
            catch (Exception ex)
            {
                // Resguardo final: pase lo que pase, el gateway no lanza
                _logger.LogError(ex, "[gRPC] TraceId={TraceId} Falla inesperada al preparar el cobro", traceId);
                resultado = Caido("Error inesperado al preparar el cobro");
            }

            string estado = resultado.Exitoso ? "Aprobado" : resultado.ServicioCaido ? "PagoNoDisponible" : "PagoFallido";
            _logger.LogInformation(
                "{Etapa} {Operacion} TraceId={TraceId} Resultado={Resultado} DuracionMs={DuracionMs}",
                "Fin", Operacion, traceId, estado, cronometro.ElapsedMilliseconds);
            return resultado;
        }

        private async Task<ResultadoCobroDto> CobrarAsync(Reserva reserva, decimal monto, string traceId)
        {
            var request = new ProcesarPagoRequest
            {
                ReservaId = reserva.ReservaId.ToString(),
                SolicitudId = reserva.SolicitudId.ToString(),
                UsuarioId = reserva.UsuarioId,
                // El precio es numeric(18,2): por 100 da un entero exacto de centavos.
                // checked: si no entrara en un long, falla en vez de cobrar un monto equivocado.
                MontoCentavos = checked((long)decimal.Round(monto * 100m, 0, MidpointRounding.AwayFromZero))
            };

            string token;
            try
            {
                // Fuera del pipeline: si Keycloak falla, no es culpa de Payment y no debe abrir su
                // circuito. Con su propio tope, para que un Keycloak colgado no frene la reserva.
                using var limite = new CancellationTokenSource(EsperaMaximaDelToken);
                token = await _tokenProvider.ObtenerTokenAsync(limite.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[KEYCLOAK] TraceId={TraceId} No se pudo obtener el token para llamar a Payment", traceId);
                return Caido("No se pudo obtener el token de Keycloak para llamar a Payment");
            }

            ResilienceContext contexto = ResilienceContextPool.Shared.Get();
            contexto.Properties.Set(ResilienciaPago.ClaveTraceId, traceId);
            try
            {
                ProcesarPagoResponse response = await _pipeline.ExecuteAsync(async ctx =>
                {
                    // La metadata se arma en cada intento. El token va en el header authorization:
                    // las CallCredentials de gRPC solo se mandan sobre TLS, y dentro de Docker la
                    // llamada va sin TLS (h2c).
                    var headers = new Metadata
                    {
                        { "authorization", $"Bearer {token}" },
                        { "x-trace-id", traceId }
                    };

                    // ctx.CancellationToken es el de Polly: cuando vence un timeout, cancela la llamada
                    return await _paymentClient.ProcesarPagoAsync(request, headers, cancellationToken: ctx.CancellationToken);
                }, contexto);

                return Mapear(response, traceId);
            }
            catch (BrokenCircuitException)
            {
                // Puede llegar antes del primer intento o después de algunos: el circuito se abrió
                // en el medio y cortó los reintentos que quedaban
                _logger.LogWarning("[POLLY] TraceId={TraceId} Circuito abierto: no se hacen más intentos a Payment", traceId);
                return Caido("Payment no está disponible (circuito abierto)");
            }
            catch (TimeoutRejectedException)
            {
                _logger.LogError("[POLLY] TraceId={TraceId} Payment no respondió a tiempo", traceId);
                return Caido("Payment no respondió a tiempo");
            }
            catch (RpcException ex)
            {
                if (ex.StatusCode == StatusCode.Unauthenticated)
                {
                    // El token pudo vencer o haber sido revocado: el próximo cobro pide uno nuevo
                    _tokenProvider.Invalidar();
                }
                // El status puede venir de Payment o del propio cliente (por ejemplo, Unavailable
                // cuando no hay nadie escuchando): el detalle dice cuál
                _logger.LogError(
                    "[gRPC] TraceId={TraceId} La llamada a Payment falló con {StatusCode}: {Detalle}",
                    traceId, ex.StatusCode, ex.Status.Detail);
                return Caido($"Error de comunicación con Payment ({ex.StatusCode})");
            }
            catch (Exception ex)
            {
                // Resguardo ante cualquier otra falla (red, serialización): nunca se propaga
                _logger.LogError(ex, "[gRPC] TraceId={TraceId} Falla inesperada al llamar a Payment", traceId);
                return Caido("Error inesperado al llamar a Payment");
            }
            finally
            {
                ResilienceContextPool.Shared.Return(contexto);
            }
        }

        private ResultadoCobroDto Mapear(ProcesarPagoResponse response, string traceId)
        {
            switch (response.Resultado)
            {
                case ResultadoPago.Aprobado:
                    return new ResultadoCobroDto { Exitoso = true, TransaccionId = response.TransaccionId };

                case ResultadoPago.RechazadoFondosInsuficientes:
                    // Error de negocio: Payment funcionó y dijo que no. Es PagoFallido, no PagoNoDisponible
                    _logger.LogWarning(
                        "[gRPC] TraceId={TraceId} Payment rechazó el cobro: {Motivo}", traceId, response.MensajeError);
                    return new ResultadoCobroDto
                    {
                        Exitoso = false,
                        ServicioCaido = false,
                        TransaccionId = response.TransaccionId,
                        ErrorMensaje = response.MensajeError
                    };

                default:
                    // Un resultado que el contrato no define es una falla técnica, no un rechazo
                    _logger.LogError(
                        "[gRPC] TraceId={TraceId} Payment devolvió un resultado desconocido: {Resultado}", traceId, response.Resultado);
                    return Caido($"Payment devolvió un resultado desconocido ({response.Resultado})");
            }
        }

        private static ResultadoCobroDto Caido(string mensaje) =>
            new() { Exitoso = false, ServicioCaido = true, ErrorMensaje = mensaje };
    }
}
