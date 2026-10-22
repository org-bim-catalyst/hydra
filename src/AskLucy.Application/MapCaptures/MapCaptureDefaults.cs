namespace AskLucy.Application.MapCaptures;

/// <summary>
/// The capture defaults an administrator has configured, resolved into the form the capture
/// pipeline uses. Every value is already valid: the reader falls back to the catalogue's default
/// for a setting that was never saved, and ignores a saved value that is no longer valid.
/// </summary>
/// <param name="Source">Which imagery provider serves the capture.</param>
/// <param name="CropPixels">How many pixels are cropped from each of the four edges.</param>
/// <param name="Zoom">The zoom level, or null when the requested view size decides it.</param>
/// <param name="EnhanceGoogleColour">Whether Google's web tone-mapping is corrected on the way out.</param>
public sealed record MapCaptureDefaults(
    MapCaptureSource Source,
    int CropPixels,
    int? Zoom,
    bool EnhanceGoogleColour)
{
    /// <summary>What applies when nothing has been configured: Esri, a 32 px crop, zoom-to-fit, and the colour correction on.</summary>
    public static MapCaptureDefaults Default { get; } = new(
        MapCaptureSource.Esri,
        32,
        null,
        true);
}
