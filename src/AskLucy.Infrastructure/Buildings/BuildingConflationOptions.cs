namespace AskLucy.Infrastructure.Buildings;

/// <summary>
/// specs/076 — how long the footprint sources are waited on once the preferred one has answered.
/// Bound from the optional "Buildings:Conflation" section; the default needs no configuration.
/// </summary>
public sealed class BuildingConflationOptions
{
    public const string SectionName = "Buildings:Conflation";

    /// <summary>
    /// After the source whose footprints are kept has answered, the lower-priority sources get this
    /// much longer to fill its gaps before they are cancelled. Overpass alone can spend a minute and
    /// a half on retries, and a slow gap-filler must not hold up buildings that are already known.
    /// </summary>
    public TimeSpan StragglerBudget { get; set; } = TimeSpan.FromSeconds(8);
}
