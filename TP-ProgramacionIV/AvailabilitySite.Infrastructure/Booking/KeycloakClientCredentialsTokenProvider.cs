using System.Net.Http.Json;
using System.Text.Json;
using AvailabilitySite.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace AvailabilitySite.Infrastructure.Booking;

public sealed class KeycloakClientCredentialsTokenProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<KeycloakServiceOptions> options) : IAccessTokenProvider, IDisposable
{
    private static readonly TimeSpan RenewalMargin = TimeSpan.FromSeconds(60);
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private readonly KeycloakServiceOptions settings = options.Value;
    private string? accessToken;
    private DateTimeOffset expiresAtUtc;

    public async Task<string?> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        if (IsUsable())
        {
            return accessToken;
        }

        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (IsUsable())
            {
                return accessToken;
            }

            return await RequestTokenAsync(cancellationToken);
        }
        finally
        {
            refreshLock.Release();
        }
    }

    private bool IsUsable() =>
        !string.IsNullOrWhiteSpace(accessToken) &&
        expiresAtUtc > DateTimeOffset.UtcNow.Add(RenewalMargin);

    private async Task<string?> RequestTokenAsync(CancellationToken cancellationToken)
    {
        var endpoint = $"{settings.Authority.TrimEnd('/')}/protocol/openid-connect/token";
        using var content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", settings.ClientId),
            new KeyValuePair<string, string>("client_secret", settings.ClientSecret)
        ]);

        try
        {
            using var response = await httpClientFactory
                .CreateClient("KeycloakService")
                .PostAsync(endpoint, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                ClearToken();
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
            if (payload is null || string.IsNullOrWhiteSpace(payload.AccessToken) || payload.ExpiresIn <= 0)
            {
                ClearToken();
                return null;
            }

            accessToken = payload.AccessToken;
            expiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(payload.ExpiresIn);
            return accessToken;
        }
        catch (JsonException)
        {
            ClearToken();
            return null;
        }
        catch (HttpRequestException)
        {
            ClearToken();
            return null;
        }
    }

    private void ClearToken()
    {
        accessToken = null;
        expiresAtUtc = DateTimeOffset.MinValue;
    }

    public void Dispose() => refreshLock.Dispose();

    private sealed class TokenResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }
    }
}
