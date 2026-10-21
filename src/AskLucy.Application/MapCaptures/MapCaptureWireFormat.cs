namespace AskLucy.Application.MapCaptures;

/// <summary>
/// The wire spellings of mode and overlays are lower-case and hyphenated; default ASP.NET model
/// binding only accepts the C# member name, so parsing is explicit (research D1).
/// </summary>
public static class MapCaptureWireFormat
{
    public static bool TryParseMode(string? value, out MapCaptureMode mode)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            mode = MapCaptureMode.Satellite;
            return true;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "roadmap":
                mode = MapCaptureMode.Roadmap;
                return true;
            case "satellite":
                mode = MapCaptureMode.Satellite;
                return true;
            case "hybrid":
                mode = MapCaptureMode.Hybrid;
                return true;
            case "buildings-only":
                mode = MapCaptureMode.BuildingsOnly;
                return true;
            default:
                mode = MapCaptureMode.Satellite;
                return false;
        }
    }

    public static bool TryParseOverlays(
        string? value, out IReadOnlyList<MapCaptureOverlay> overlays, out string? invalidName)
    {
        var parsed = new List<MapCaptureOverlay>();
        invalidName = null;

        if (!string.IsNullOrWhiteSpace(value))
        {
            foreach (var token in value.Split(
                ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                switch (token.ToLowerInvariant())
                {
                    case "building-footprints":
                        parsed.Add(MapCaptureOverlay.BuildingFootprints);
                        break;
                    case "site-boundary":
                        parsed.Add(MapCaptureOverlay.SiteBoundary);
                        break;
                    default:
                        overlays = parsed;
                        invalidName = token;
                        return false;
                }
            }
        }

        overlays = parsed.Distinct().ToArray();
        return true;
    }
}
