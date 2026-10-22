namespace AskLucy.Application.MapCaptures;

/// <summary>
/// Fully resolved capture request — the handler has already applied defaults.
/// <para>
/// <c>Source</c> is the imagery provider that serves the capture; <c>CropPerEdge</c> is how many
/// pixels are removed from each of the four edges of the provider's frame; <c>Zoom</c> is the
/// framing level, or null when the requested view size decides it; and <c>EnhanceColour</c>
/// says whether a web tone-mapped provider's output is corrected before delivery.
/// </para>
/// </summary>
public sealed record MapImageRequest(
    double Latitude,
    double Longitude,
    string LocationName,
    MapCaptureMode Mode,
    IReadOnlyList<MapCaptureOverlay> Overlays,
    int ViewSizeMetres,
    string CallerName,
    MapCaptureSource Source = MapCaptureSource.Esri,
    int CropPerEdge = 32,
    int? Zoom = null,
    bool EnhanceColour = true);
