namespace AvailabilitySite.Infrastructure.Booking;

public sealed class KeycloakServiceOptions
{
    public const string SectionName = "KeycloakService";

    public string Authority { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}
