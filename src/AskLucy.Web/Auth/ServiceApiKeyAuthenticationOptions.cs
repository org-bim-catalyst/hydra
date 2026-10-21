using Microsoft.AspNetCore.Authentication;

namespace AskLucy.Web.Auth;

/// <summary>
/// specs/083-viewer-map-capture T023 — options for the service-to-service API key scheme.
/// </summary>
public sealed class ServiceApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string SchemeName = "ServiceApiKey";

    /// <summary>The header the caller presents the key in.</summary>
    public string HeaderName { get; set; } = "X-Api-Key";
}
