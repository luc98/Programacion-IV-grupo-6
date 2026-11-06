using AvailabilitySite.Application.Reservations;
using AvailabilitySite.Application.Common;
using AvailabilitySite.Infrastructure.Persistence;
using AvailabilitySite.Web.Models;
using AvailabilitySite.Web.ExceptionHandling;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AvailabilitySite.Application.Tests;

public sealed class AvailabilityPersistenceExceptionHandlerTests
{
    [Fact]
    public async Task TransientPersistenceException_Returns503WithGenericProblemDetails()
    {
        var context = CreateContext("/reservations");
        var handler = CreateHandler();
        var exception = new AvailabilityPersistenceException(
            "La base de datos de Availability no está disponible en este momento.",
            isTransient: true,
            new InvalidOperationException("mongodb.internal:27017 connection details"));

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains(AvailabilityPersistenceExceptionHandler.PublicMessage, body);
        Assert.DoesNotContain("mongodb.internal", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connection details", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(context.TraceIdentifier, body);
    }

    [Fact]
    public async Task NonTransientDuplicateRequest_IsNotConvertedTo503()
    {
        var context = CreateContext("/reservations");
        var handler = CreateHandler();
        var existing = new ReservationRequestRecord(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "user", "key", DateTimeOffset.UtcNow,
            ReservationRequestStatus.Pending);

        var handled = await handler.TryHandleAsync(
            context,
            new DuplicateReservationRequestException(existing),
            CancellationToken.None);

        Assert.False(handled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task UnrelatedException_IsNotConvertedTo503()
    {
        var context = CreateContext("/events");
        var handler = CreateHandler();

        var handled = await handler.TryHandleAsync(
            context,
            new InvalidOperationException("unexpected application failure"),
            CancellationToken.None);

        Assert.False(handled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Theory]
    [InlineData(ApplicationErrorType.Validation, 400)]
    [InlineData(ApplicationErrorType.NotFound, 404)]
    [InlineData(ApplicationErrorType.Forbidden, 403)]
    public void ExistingApplicationErrors_KeepTheirHttpStatusCodes(ApplicationErrorType errorType, int expectedStatus)
    {
        var controller = CreateController();

        var result = controller.Translate(new ApplicationError(errorType, "test_error", "Mensaje de prueba."));

        Assert.IsType<ViewResult>(result);
        Assert.Equal(expectedStatus, controller.HttpContext.Response.StatusCode);
    }

    private static AvailabilityPersistenceExceptionHandler CreateHandler() =>
        new(NullLogger<AvailabilityPersistenceExceptionHandler>.Instance);

    private static DefaultHttpContext CreateContext(string path)
    {
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "trace-test-123",
        };
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static TestController CreateController()
    {
        var context = new DefaultHttpContext();
        var actionContext = new ActionContext(
            context,
            new RouteData(),
            new ControllerActionDescriptor());
        return new TestController
        {
            ControllerContext = new ControllerContext(actionContext),
        };
    }

    private sealed class TestController : AvailabilitySite.Web.Controllers.AppController
    {
        public IActionResult Translate(ApplicationError error) =>
            ErrorPage(error, links: new PageLink("Test", "/test"));
    }
}
