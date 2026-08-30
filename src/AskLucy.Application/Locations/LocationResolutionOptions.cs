namespace AskLucy.Application.Locations;

/// <summary>
/// specs/037-location-query-resolution — Application-layer configuration for the location
/// resolution pipeline. Bound from the "LocationResolution" appsettings section.
/// </summary>
public sealed class LocationResolutionOptions
{
    public const string SectionName = "LocationResolution";

    /// <summary>
    /// FR-013 ceiling: maximum seconds the response handler waits for geocoding to complete,
    /// measured from the start of the turn — so the model's own streaming time is spent out of
    /// this budget before geocoding is even awaited.
    /// <para>
    /// 30, matching the "Geocoding" HttpClient's timeout (widened from 15 s in ee8ecb2 after
    /// slow-but-successful calls from this host were being aborted client-side). Leaving this at
    /// 15 while the client allowed 30 meant a geocoding call that succeeded in 16–30 s had its
    /// result discarded here as <c>Unavailable</c> — the exact class of call ee8ecb2 set out to
    /// rescue.
    /// </para>
    /// </summary>
    public int ResolutionCeilingSeconds { get; set; } = 30;

    /// <summary>
    /// Candidates scoring below this are discarded before the dominance check.
    /// <para>
    /// Calibrated against Nominatim's <c>importance</c>, which is a Wikipedia-linkage popularity
    /// score, NOT a match-quality score: world-famous places score high (Burj Khalifa 0.56), but
    /// an ordinary correctly-matched local feature scores ~0.06–0.09 (Al Safa Park 2, Dubai:
    /// 0.0801), while genuinely irrelevant matches — a side street that merely shares the name —
    /// come back at exactly 0.0. A floor of 0.1 therefore rejected almost every real local place
    /// while admitting nothing extra, which is why "show me Al Safa Park 2 in the viewer"
    /// returned NotFound and the viewer never moved. 0.05 keeps the 0.0 junk out.
    /// </para>
    /// <para>
    /// <see cref="AskLucy.Application.Locations.IGeocodingProvider"/> implementations are
    /// expected to report on this same scale; the Google Maps adapter deliberately maps
    /// <c>location_type</c> into 0.40–0.90 so this threshold applies unchanged.
    /// </para>
    /// </summary>
    public double MinimumImportanceFloor { get; set; } = 0.05;

    /// <summary>
    /// FR-006: how far the top candidate must outscore the runner-up to be taken as the single
    /// intended place. Below this the query is reported as Ambiguous rather than guessed at.
    /// </summary>
    public double CandidateDominanceMargin { get; set; } = 0.2;
}
