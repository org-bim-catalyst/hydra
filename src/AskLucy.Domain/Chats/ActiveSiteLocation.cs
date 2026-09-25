namespace AskLucy.Domain.Chats;

/// <summary>
/// specs/037-location-query-resolution — the agent-confirmed location that the viewer is
/// currently centred on. Owned by <see cref="UserChat"/> so it survives the turn boundary
/// and back-references (FR-014) can resolve without a new geocoding call.
/// <para>
/// <see cref="LocationType"/> is the geocoder's precision code the confidence level is read from;
/// kept so a back-reference or a reopened chat shows the same level as the live confirmation did.
/// Null when the geocoder reported none, or for a location recorded before it was kept.
/// </para>
/// </summary>
public sealed record ActiveSiteLocation(
    double Latitude,
    double Longitude,
    string LocationName,
    double Confidence,
    string? LocationType = null);
