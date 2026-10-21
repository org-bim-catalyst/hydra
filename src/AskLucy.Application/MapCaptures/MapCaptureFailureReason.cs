namespace AskLucy.Application.MapCaptures;

public enum MapCaptureFailureReason
{
    /// <summary>Geocoding returned zero candidates.</summary>
    LocationNotFound,

    /// <summary>Geocoding provider failed or timed out.</summary>
    GeocodingUnavailable,

    /// <summary>The requested mode cannot be produced by the configured provider.</summary>
    ModeUnavailable,

    /// <summary>The requested overlay cannot be produced for this location or path.</summary>
    OverlayUnavailable,

    /// <summary>Imagery provider failed, timed out, or returned a non-image.</summary>
    ProviderUnavailable,

    /// <summary>The provider image is ≤ 28 px in either dimension.</summary>
    CropTooSmall,

    /// <summary>The encoded PNG exceeds <see cref="MapCaptureOptions.MaxImageBytes"/>.</summary>
    ImageTooLarge,
}
