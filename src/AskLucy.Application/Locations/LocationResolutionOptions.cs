namespace AskLucy.Application.Locations;

/// <summary>
/// specs/037-location-query-resolution — Application-layer configuration for the location
/// resolution pipeline. Bound from the "LocationResolution" appsettings section.
/// <see cref="ResolutionCeilingSeconds"/> matches the "Weather" HttpClient's already-configured
/// 15 s timeout and is the named constant that <c>SendChatMessageCommandHandler</c> uses to
/// avoid an inline literal (constitution §4).
/// </summary>
public sealed class LocationResolutionOptions
{
    public const string SectionName = "LocationResolution";

    /// <summary>
    /// FR-013 ceiling: maximum seconds the response handler waits for geocoding to complete
    /// after the model's text stream ends. Defaults to 15 to match the geocoding HttpClient's
    /// timeout.
    /// </summary>
    public int ResolutionCeilingSeconds { get; set; } = 15;
}
