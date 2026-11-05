namespace AvailabilitySite.Application.Abstractions;

public interface ICurrentUserContext
{
    bool IsAuthenticated { get; }

    string? UserId { get; }
}
