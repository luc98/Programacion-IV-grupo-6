namespace AvailabilitySite.Application.Abstractions;

public interface IAccessTokenProvider
{
    Task<string?> GetTokenAsync(CancellationToken cancellationToken = default);
}
