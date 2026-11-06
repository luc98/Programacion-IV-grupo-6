using System.Diagnostics;
using AvailabilitySite.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace AvailabilitySite.Web.ExceptionHandling;

public sealed class AvailabilityPersistenceExceptionHandler(
    ILogger<AvailabilityPersistenceExceptionHandler> logger) : IExceptionHandler
{
    public const string PublicMessage =
        "El servicio de disponibilidad no está disponible temporalmente. Intentá nuevamente más tarde";

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not AvailabilityPersistenceException { IsTransient: true } persistenceException)
            return false;

        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;
        logger.LogError(
            persistenceException,
            "Error transitorio de persistencia en Availability. TraceId: {TraceId}. Path: {Path}",
            traceId,
            httpContext.Request.Path);

        if (httpContext.Response.HasStarted)
            return false;

        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        httpContext.Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title = "Servicio de disponibilidad no disponible",
            Detail = PublicMessage,
            Instance = httpContext.Request.Path,
        };
        problem.Extensions["traceId"] = traceId;

        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}
