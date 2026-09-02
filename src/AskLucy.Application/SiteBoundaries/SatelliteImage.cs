namespace AskLucy.Application.SiteBoundaries;

/// <summary>
/// specs/042-site-boundary-resolution — a satellite image crop plus the exact geographic bounds
/// it covers, mirroring the reference notebook's <c>image_context</c> dict. The bounds let a
/// vision analyzer relate what it sees back to real coordinates without being handed any
/// coordinates it could mistake for permission to invent new ones.
/// </summary>
public sealed record SatelliteImage(
    byte[] ImageBytes,
    string ContentType,
    double West,
    double South,
    double East,
    double North);
