namespace AvailabilitySite.Web.Authentication;

public sealed class KeycloakOptions
{
    public const string SectionName = "Keycloak";

    public string Authority { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string CallbackPath { get; set; } = "/signin-oidc";
    public string SignedOutCallbackPath { get; set; } = "/signout-callback-oidc";
    public bool RequireHttpsMetadata { get; set; }
    public string? MetadataAddress { get; set; }
}
