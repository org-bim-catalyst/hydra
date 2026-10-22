namespace AskLucy.Application.MapCaptures;

/// <summary>
/// Which imagery provider serves a capture. Both return the same underlying photography; they
/// differ in how it is rendered and branded.
/// </summary>
public enum MapCaptureSource
{
    /// <summary>
    /// Esri World Imagery: analysis-grade, saturated, and without a burned-in logo — but served
    /// from a bbox export, which cannot be assembled past <see cref="MapCaptureZoom.MaximumEsri"/>.
    /// </summary>
    Esri,

    /// <summary>
    /// Google Static Maps: the basemap the viewer itself shows, so a capture matches what the
    /// caller is looking at — but tone-mapped for a browser and stamped with Google's logo and
    /// the imagery attribution.
    /// </summary>
    Google,
}

/// <summary>The wire spelling of a source, and the parser for it.</summary>
public static class MapCaptureSourceWire
{
    public const string Esri = "esri";
    public const string Google = "google";

    public static string WireValueOf(MapCaptureSource source) => source switch
    {
        MapCaptureSource.Google => Google,
        _ => Esri,
    };

    /// <summary>Parses the wire spelling. Case-insensitive, and null-safe: a missing source is Esri.</summary>
    public static bool TryParse(string? value, out MapCaptureSource source)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case null or "":
            case Esri:
                source = MapCaptureSource.Esri;
                return true;
            case Google:
                source = MapCaptureSource.Google;
                return true;
            default:
                source = MapCaptureSource.Esri;
                return false;
        }
    }
}
