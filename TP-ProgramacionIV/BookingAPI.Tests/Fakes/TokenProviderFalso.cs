using BookingAPI.BLL.Interfaces;

namespace BookingAPI.Tests.Fakes;

public sealed class TokenProviderFalso : ITokenProvider
{
    public const string Token = "token-de-prueba";

    public bool Falla { get; set; }

    public int Invalidaciones { get; private set; }

    public Task<string> ObtenerTokenAsync(CancellationToken cancellationToken) =>
        Falla
            ? Task.FromException<string>(new HttpRequestException("Keycloak no responde"))
            : Task.FromResult(Token);

    public void Invalidar() => Invalidaciones++;
}
