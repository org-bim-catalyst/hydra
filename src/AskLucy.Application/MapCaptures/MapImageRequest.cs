namespace AskLucy.Application.MapCaptures;

/// <summary>Fully resolved capture request — the handler has already applied defaults.</summary>
public sealed record MapImageRequest(
    double Latitude,
    double Longitude,
    string LocationName,
    MapCaptureMode Mode,
    IReadOnlyList<MapCaptureOverlay> Overlays,
    int ViewSizeMetres,
    string CallerName);
