using System.Net;
using System.Net.Http.Headers;
using AvailabilitySite.Infrastructure.Booking;
using Microsoft.Extensions.Options;
using Xunit;

namespace AvailabilitySite.Application.Tests;

public sealed class KeycloakClientCredentialsTokenProviderTests
{
    [Fact]
    public async Task GetTokenAsync_SendsClientCredentialsAndCachesToken()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "token-1", 3600));
        using var provider = CreateProvider(handler);

        var first = await provider.GetTokenAsync();
        var second = await provider.GetTokenAsync();

        Assert.Equal("token-1", first);
        Assert.Equal(first, second);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal("grant_type=client_credentials&client_id=availability-api&client_secret=test-secret", handler.RequestBody);
        Assert.Equal("application/x-www-form-urlencoded", handler.ContentType);
    }

    [Fact]
    public async Task GetTokenAsync_RefreshesTokenNearExpiration()
    {
        var handler = new StubHandler(call => call == 1
            ? Json(HttpStatusCode.OK, "token-1", 1)
            : Json(HttpStatusCode.OK, "token-2", 3600));
        using var provider = CreateProvider(handler);

        var first = await provider.GetTokenAsync();
        var second = await provider.GetTokenAsync();

        Assert.Equal("token-1", first);
        Assert.Equal("token-2", second);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task GetTokenAsync_ConcurrentCallsRequestOneToken()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "token-1", 3600), TimeSpan.FromMilliseconds(50));
        using var provider = CreateProvider(handler);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => provider.GetTokenAsync()));

        Assert.All(results, token => Assert.Equal("token-1", token));
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GetTokenAsync_AuthenticationErrorReturnsNull()
    {
        using var provider = CreateProvider(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        var token = await provider.GetTokenAsync();

        Assert.Null(token);
    }

    [Fact]
    public async Task GetTokenAsync_CancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new StubHandler(_ => throw new OperationCanceledException(cancellation.Token));
        using var provider = CreateProvider(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetTokenAsync(cancellation.Token));
    }

    private static KeycloakClientCredentialsTokenProvider CreateProvider(HttpMessageHandler handler) =>
        new(new StubHttpClientFactory(handler), Options.Create(new KeycloakServiceOptions
        {
            Authority = "http://localhost:8080/realms/reservas-realm",
            ClientId = "availability-api",
            ClientSecret = "test-secret"
        }));

    private static HttpResponseMessage Json(HttpStatusCode status, string token, int expiresIn) =>
        new(status)
        {
            Content = new StringContent($"{{\"access_token\":\"{token}\",\"expires_in\":{expiresIn}}}", System.Text.Encoding.UTF8, "application/json")
        };

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private sealed class StubHandler(
        Func<int, HttpResponseMessage> responseFactory,
        TimeSpan? delay = null) : HttpMessageHandler
    {
        private int calls;
        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }
        public string? ContentType { get; private set; }
        public int CallCount => calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            var call = Interlocked.Increment(ref calls);
            if (delay.HasValue)
            {
                await Task.Delay(delay.Value, cancellationToken);
            }

            return responseFactory(call);
        }
    }
}
