using System.Diagnostics;

namespace BookingAPI.Presentation.Middlewares
{
    // Middleware HTTP alineado con la convención grupal de trazabilidad (docs/plantilla-de-logs.md).
    // Extrae el identificador 'x-trace-id' entrante (o reutiliza/genera uno si inicia aquí),
    // lo adjunta al alcance de logs (compatible con ILogger y Serilog FromLogContext)
    // y registra el Inicio y Fin de cada solicitud HTTP con su código de estado y duración en ms.
    public sealed class TraceIdMiddleware
    {
        public const string HeaderName = "x-trace-id";
        public const string PropertyName = "TraceId";

        private readonly RequestDelegate _next;

        public TraceIdMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, ILogger<TraceIdMiddleware> logger)
        {
            string? traceId = context.Request.Headers[HeaderName].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(traceId))
            {
                traceId = Guid.NewGuid().ToString();
            }

            context.Items[PropertyName] = traceId;
            context.Response.Headers[HeaderName] = traceId;

            using (logger.BeginScope(new Dictionary<string, object> { [PropertyName] = traceId }))
            {
                string operacion = $"{context.Request.Method} {context.Request.Path}";
                var cronometro = Stopwatch.StartNew();

                logger.LogInformation("{Etapa} {Operacion} TraceId={TraceId}", "Inicio", operacion, traceId);

                try
                {
                    await _next(context);
                }
                finally
                {
                    logger.LogInformation(
                        "{Etapa} {Operacion} TraceId={TraceId} Resultado={Resultado} DuracionMs={DuracionMs}",
                        "Fin",
                        operacion,
                        traceId,
                        context.Response.StatusCode,
                        cronometro.ElapsedMilliseconds);
                }
            }
        }
    }
}
