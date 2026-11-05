using BookingAPI.BLL.Options;
using Grpc.Core;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace BookingAPI.BLL.Gateways
{
    // [JUSTIFICACIÓN TEÓRICA - RESILIENCIA CON POLLY v8 (PUNTO 3.j)]
    // Pipeline de resiliencia de la llamada a Payment. Las estrategias se aplican de afuera hacia
    // adentro, como capas de una cebolla:
    //
    //   1. Timeout total (10 s): tope de todo el cobro, reintentos incluidos.
    //   2. Retry (2 reintentos, espera exponencial desde 500 ms con jitter): solo ante fallas
    //      transitorias, donde reintentar tiene sentido y es seguro (Payment es idempotente).
    //   3. Circuit breaker: si Payment viene fallando, deja de llamarlo por un rato y rechaza al
    //      instante (fail-fast), en lugar de hacer esperar cada reserva y cargar a un servicio caído.
    //   4. Timeout por intento (2 s): corta un intento colgado y cancela la llamada gRPC. Si Payment
    //      todavía no había guardado el pago, no hay cobro. Si ya lo había guardado, el reintento lo
    //      encuentra por reserva_id (idempotencia). Queda un caso: que el corte caiga en el último
    //      intento justo después de guardar. Ahí Booking rechaza y Payment tiene el pago: es la
    //      frontera de consistencia entre las dos bases (punto 3.g), que en un sistema real se
    //      resuelve conciliando contra Payment.
    //
    // El circuit breaker va adentro del retry para que cuente cada intento por separado.
    //
    // Es Singleton a propósito: el circuit breaker guarda en memoria el historial de fallas. Si se
    // armara uno por request (Scoped), cada cobro empezaría con el circuito "limpio" y nunca se abriría.
    public class ResilienciaPago
    {
        // El TraceId viaja en el contexto de Polly para que los logs de reintentos y del circuito
        // lo incluyan
        public static readonly ResiliencePropertyKey<string> ClaveTraceId = new("TraceId");

        public ResiliencePipeline Pipeline { get; }

        public ResilienciaPago(IOptions<ResilienciaPagoOptions> opciones, ILogger<ResilienciaPago> logger)
        {
            Pipeline = Construir(opciones.Value, logger);
        }

        public static ResiliencePipeline Construir(ResilienciaPagoOptions opciones, ILogger logger)
        {
            // Qué cuenta como falla transitoria: Payment no disponible (caído, reiniciando, red cortada)
            // o un intento que superó su timeout. Un error de negocio no llega acá: fondos insuficientes
            // es una respuesta OK. Tampoco se reintentan InvalidArgument, Unauthenticated, Internal ni
            // DeadlineExceeded: repetirlos no cambia el resultado, o no se sabe si Payment ya cobró.
            var esTransitoria = new PredicateBuilder()
                .Handle<RpcException>(ex => ex.StatusCode == StatusCode.Unavailable)
                .Handle<TimeoutRejectedException>();

            return new ResiliencePipelineBuilder()
                .AddTimeout(new TimeoutStrategyOptions
                {
                    Timeout = TimeSpan.FromSeconds(opciones.TimeoutTotalSegundos),
                    OnTimeout = args =>
                    {
                        logger.LogWarning(
                            "[POLLY] TraceId={TraceId} El cobro superó el timeout total de {Timeout} s",
                            TraceId(args.Context), args.Timeout.TotalSeconds);
                        return default;
                    }
                })
                .AddRetry(new RetryStrategyOptions
                {
                    ShouldHandle = esTransitoria,
                    MaxRetryAttempts = opciones.Reintentos,
                    BackoffType = DelayBackoffType.Exponential,
                    Delay = TimeSpan.FromMilliseconds(opciones.DemoraInicialMs),
                    MaxDelay = TimeSpan.FromMilliseconds(opciones.DemoraMaximaMs),
                    // El jitter desparrama los reintentos: si muchas reservas fallan juntas, no
                    // vuelven a golpear a Payment todas en el mismo instante.
                    UseJitter = true,
                    OnRetry = args =>
                    {
                        logger.LogWarning(
                            "[POLLY] TraceId={TraceId} Reintento {Intento} a Payment en {DemoraMs} ms. Causa: {Causa}",
                            TraceId(args.Context), args.AttemptNumber + 1, (int)args.RetryDelay.TotalMilliseconds,
                            Describir(args.Outcome.Exception));
                        return default;
                    }
                })
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions
                {
                    ShouldHandle = esTransitoria,
                    FailureRatio = opciones.ProporcionDeFallas,
                    MinimumThroughput = opciones.MinimoDeLlamadas,
                    SamplingDuration = TimeSpan.FromSeconds(opciones.VentanaSegundos),
                    BreakDuration = TimeSpan.FromSeconds(opciones.CorteSegundos),
                    OnOpened = args =>
                    {
                        logger.LogError(
                            "[POLLY] TraceId={TraceId} Circuito ABIERTO: Payment viene fallando. Se rechazan los cobros sin llamarlo durante {Segundos} s",
                            TraceId(args.Context), args.BreakDuration.TotalSeconds);
                        return default;
                    },
                    OnHalfOpened = args =>
                    {
                        logger.LogWarning(
                            "[POLLY] TraceId={TraceId} Circuito SEMIABIERTO: se prueba una llamada a Payment",
                            TraceId(args.Context));
                        return default;
                    },
                    OnClosed = args =>
                    {
                        logger.LogInformation(
                            "[POLLY] TraceId={TraceId} Circuito CERRADO: Payment volvió a responder",
                            TraceId(args.Context));
                        return default;
                    }
                })
                .AddTimeout(TimeSpan.FromSeconds(opciones.TimeoutPorIntentoSegundos))
                .Build();
        }

        private static string TraceId(ResilienceContext context) =>
            context.Properties.GetValue(ClaveTraceId, string.Empty);

        private static string Describir(Exception? ex) => ex switch
        {
            RpcException rpc => $"gRPC {rpc.StatusCode}",
            TimeoutRejectedException => "timeout del intento",
            null => "sin excepción",
            _ => ex.GetType().Name
        };
    }
}
