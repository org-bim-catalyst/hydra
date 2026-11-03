namespace AskLucy.Application.SiteAnalysis;

/// <summary>Length of the site's boundary that fronts one identifiable street (FR-010).</summary>
public sealed record StreetFrontage(string StreetName, double LengthMeters);

/// <summary>
/// specs/089-site-geometry-specialist data-model.md § 4 — the measured geometry of one site.
/// <see cref="OrientationBearingDegrees"/> is null when the site is near-square and a principal
/// direction would be an arbitrary choice rather than a measurement; <see cref="LooksLikeResolvedBuffer"/>
/// is true when the ring is the platform's own approximate fallback circle, which the finding says
/// out loud rather than presenting as a measured parcel.
/// </summary>
public sealed record SiteGeometryMeasurements(
    double AreaSquareMeters,
    double LengthMeters,
    double WidthMeters,
    double? OrientationBearingDegrees,
    IReadOnlyList<StreetFrontage> Frontages,
    bool LooksLikeResolvedBuffer)
{
    /// <summary>Total street-facing length — the sum of <see cref="Frontages"/>.</summary>
    public double TotalFrontageMeters => Frontages.Sum(frontage => frontage.LengthMeters);
}
