namespace AskLucy.Application.MapCaptures;

/// <summary>
/// The zoom levels a capture may be taken at, and what each provider can actually serve.
/// <para>
/// A zoom is the ground resolution of the returned image: each level halves the ground a pixel
/// covers. The numbers here are the *framing* levels; the delivered image is twice their
/// resolution because the request asks for twice the pixels over the same ground.
/// </para>
/// </summary>
public static class MapCaptureZoom
{
    /// <summary>The lowest level the platform offers — below this the imagery is too coarse to be worth looking at.</summary>
    public const int Minimum = 14;

    /// <summary>
    /// The highest level Google Static Maps serves. It clamps silently above this, so a request
    /// for more is refused rather than quietly answered with less.
    /// </summary>
    public const int Maximum = 21;

    /// <summary>
    /// The highest level Esri World Imagery's export endpoint can assemble. Above it the export
    /// service cannot composite the tiles it has and answers with a server error, so a request
    /// for more is refused rather than answered with a broken image.
    /// </summary>
    public const int MaximumEsri = 18;

    /// <summary>The highest level a given source can serve.</summary>
    public static int MaximumFor(MapCaptureSource source) =>
        source == MapCaptureSource.Google ? Maximum : MaximumEsri;

    public static bool IsInRange(int zoom) => zoom is >= Minimum and <= Maximum;

    /// <summary>Parses a wire zoom. Blank or absent means "the view size decides" (null).</summary>
    public static bool TryParse(string? value, out int? zoom)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            zoom = null;
            return true;
        }

        if (int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            zoom = parsed;
            return true;
        }

        zoom = null;
        return false;
    }
}
