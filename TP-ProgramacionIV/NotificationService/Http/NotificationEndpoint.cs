using System.Text;
using NotificationService.Core;
using NotificationService.Messaging;

namespace NotificationService.Http;

public static class NotificationEndpoint
{
    public const string Route = "/api/notifications";
    public const int MaxBodyBytes = 64 * 1024;

    public static IEndpointRouteBuilder MapNotificationEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(Route, ReceiveAsync);
        return endpoints;
    }

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
