using System.Net;
using System.Net.Http.Headers;
using AvailabilitySite.Application.Abstractions;
using AvailabilitySite.Application.Common;
using AvailabilitySite.Infrastructure.Booking;
using Xunit;

namespace AvailabilitySite.Application.Tests;

public sealed class BookingReservationStatusClientTests
{
    [Fact]
    public async Task GetAsync_Success_UsesRouteAndBearerAndMapsAllFields()
    {
        var requestId = Guid.NewGuid();
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($"{{\"reservaId\":\"{Guid.NewGuid()}\",\"solicitudId\":\"{requestId}\",\"asientoId\":\"{Guid.NewGuid()}\",\"eventoId\":\"{Guid.NewGuid()}\",\"estado\":\"Retenida\",\"motivoRechazo\":\"\",\"fechaCreacionUtc\":\"2026-10-29T12:00:00Z\",\"fechaVencimientoUtc\":\"2026-10-29T12:10:00Z\"}}")
        });
        var client = CreateClient(handler, "jwt-token");

        var result = await client.GetAsync(requestId);

        Assert.True(result.IsSuccess);
        Assert.Equal($"api/reservas/solicitudes/{requestId}", handler.Request!.RequestUri!.PathAndQuery.TrimStart('/'));
        Assert.Equal("application/json", handler.Request.Headers.Accept.Single().MediaType);
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal("jwt-token", handler.Request.Headers.Authorization.Parameter);
        Assert.Equal(requestId, result.Value!.RequestId);
        Assert.Equal("Retenida", result.Value.BookingStatus);
        Assert.Equal(new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc), result.Value.CreatedAtUtc);
        Assert.Equal(new DateTime(2026, 10, 29, 12, 10, 0, DateTimeKind.Utc), result.Value.ExpirationAtUtc);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, ApplicationErrorType.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized, ApplicationErrorType.Unauthenticated)]
    [InlineData(HttpStatusCode.Forbidden, ApplicationErrorType.Forbidden)]
    public async Task GetAsync_KnownHttpErrors_MapToApplicationErrors(
        HttpStatusCode statusCode,
        ApplicationErrorType expectedType)
    {
        var client = CreateClient(new StubHandler(_ => new HttpResponseMessage(statusCode)), "token");

        var result = await client.GetAsync(Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedType, result.Error!.Type);
    }

    [Fact]
    public async Task GetAsync_NetworkError_ReturnsDependencyUnavailable()
    {
        var client = CreateClient(new StubHandler(_ => throw new HttpRequestException()), "token");

        var result = await client.GetAsync(Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationErrorType.DependencyUnavailable, result.Error!.Type);
    }

    [Fact]
    public async Task GetAsync_Timeout_ReturnsDependencyUnavailable()
    {
        var client = CreateClient(new StubHandler(_ => throw new TaskCanceledException("timeout")), "token");

        var result = await client.GetAsync(Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationErrorType.DependencyUnavailable, result.Error!.Type);
        Assert.Equal("booking_timeout", result.Error.Code);
    }

    [Fact]
    public async Task GetAsync_Cancellation_PropagatesCancellation()
    {
        var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var client = CreateClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)), "token");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetAsync(Guid.NewGuid(), cancellation.Token));
    }

    private static BookingReservationStatusClient CreateClient(
        HttpMessageHandler handler,
        string? token)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:5156/")
        };
        return new BookingReservationStatusClient(httpClient, new StubTokenProvider(token));
    }

    private sealed class StubTokenProvider(string? token) : IAccessTokenProvider
    {
        public Task<string?> GetTokenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(token);
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(responseFactory(request));
        }
    }
}
