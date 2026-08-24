namespace AskLucy.Domain.Chats;

/// <summary>
/// specs/037-location-query-resolution — the agent-confirmed location that the viewer is
/// currently centred on. Owned by <see cref="UserChat"/> so it survives the turn boundary
/// and back-references (FR-014) can resolve without a new geocoding call.
/// </summary>
public sealed record ActiveSiteLocation(
    double Latitude,
    double Longitude,
    string LocationName,
    double Confidence);
