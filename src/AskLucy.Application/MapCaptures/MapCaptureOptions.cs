namespace AskLucy.Application.MapCaptures;

/// <summary>
/// specs/083-viewer-map-capture — Application-layer configuration for the service-to-service
/// capture endpoint. Bound from the "MapCapture" appsettings section.
/// </summary>
public sealed class MapCaptureOptions
{
    public const string SectionName = "MapCapture";

    /// <summary>
    /// Static service keys. Empty means every request is rejected with 401; it is deliberately
    /// not part of the startup validation, so a deployment without keys still starts.
    /// </summary>
    public List<MapCaptureApiKey> ApiKeys { get; set; } = [];

    /// <summary>Width of the captured area, in metres, when the caller omits <c>viewSizeMeters</c>.</summary>
    public int DefaultViewSizeMetres { get; set; } = 1000;

    public int MinViewSizeMetres { get; set; } = 100;

    public int MaxViewSizeMetres { get; set; } = 20000;

    /// <summary>Encoded PNG size cap after cropping (10 MB).</summary>
    public int MaxImageBytes { get; set; } = 10_485_760;

    public int RateLimitPerMinute { get; set; } = 60;
}

public sealed record MapCaptureApiKey(string Name, string Key);
