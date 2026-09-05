namespace AskLucy.Domain.SiteBoundaries;

/// <summary>
/// One ground-level Street View Static API frame, taken near a site's perimeter and aimed back
/// toward it — the view satellite imagery cannot give: whether a wall or fence actually runs
/// along a given edge, and which side of it something (a court, a structure) sits on.
/// </summary>
public sealed record StreetViewImage(
    byte[] ImageBytes,
    string ContentType,
    double ViewpointLatitude,
    double ViewpointLongitude,
    double HeadingDegrees);
