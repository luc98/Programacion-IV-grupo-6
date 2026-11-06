using System.Text;
using NotificationService.Core;
using NotificationService.Messaging;
using NotificationService.Security;

namespace NotificationService.Http;

// [JUSTIFICACIÓN - Adaptador de entrada] Único lugar que conoce HTTP: convierte la llamada en un IncomingEvent y el resultado en un código
// (202, 400 o 503). Se hizo así por recomendación del profesor: llamada directa de Booking, sin mezclar tópicos.
public static class NotificationEndpoint
{
    public const string Route = "/api/notifications";
    public const int MaxBodyBytes = 64 * 1024;

    public static IEndpointRouteBuilder MapNotificationEndpoint(this IEndpointRouteBuilder endpoints, bool requireAuthorization = false)
    {
        var route = endpoints.MapPost(Route, ReceiveAsync);
        if (requireAuthorization)
        {
            route.RequireAuthorization(AuthenticationExtensions.PolicyName);
        }

        return endpoints;
    }

    // [JUSTIFICACIÓN - Resultado a HTTP] Completed responde 202 (también un repetido), Rejected 400 con el motivo y Retry 503,
    // para que quien llama pueda reintentar.
    private static async Task<IResult> ReceiveAsync(
        HttpRequest request,
        IEventProcessor processor,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(cancellationToken);

        var incoming = HttpEventMapper.ToIncomingEvent(
            request.Headers[HttpEventMapper.EventTypeHeader].FirstOrDefault(),
            request.Headers[HttpEventMapper.TraceIdHeader].FirstOrDefault(),
            body);

        var result = await processor.ProcessAsync(incoming, cancellationToken);

        return result.Outcome switch
        {
            ProcessingOutcome.Completed => Results.Accepted(),
            ProcessingOutcome.Rejected => Results.BadRequest(new { error = result.Reason }),
            _ => Results.Json(new { error = result.Reason }, statusCode: StatusCodes.Status503ServiceUnavailable),
        };
    }
}
