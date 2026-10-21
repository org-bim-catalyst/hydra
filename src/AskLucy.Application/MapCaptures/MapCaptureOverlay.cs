using System.Text.Json.Serialization;

namespace AskLucy.Application.MapCaptures;

public enum MapCaptureOverlay
{
    [JsonStringEnumMemberName("building-footprints")]
    BuildingFootprints,

    /// <summary>
    /// Recognised so the failure can name it, then rejected with
    /// <see cref="MapCaptureFailureReason.OverlayUnavailable"/>: boundary resolution is
    /// chat-scoped and too slow for this path (research D5).
    /// </summary>
    [JsonStringEnumMemberName("site-boundary")]
    SiteBoundary,
}
