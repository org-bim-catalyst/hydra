using AskLucy.Application.MapCaptures;

namespace AskLucy.Application.Agents.ToolSettings;

/// <summary>
/// Every setting any agent tool has. A tool with none here shows a disabled gear on the admin AI
/// Tools page; adding one here is all it takes to enable it (values are stored as key/value rows,
/// so no migration either).
/// </summary>
public sealed class ToolSettingCatalog
{
    /// <summary>The tool the map capture settings belong to — the <c>map_snapshot</c> capability.</summary>
    public const string MapSnapshotToolName = "map_snapshot";

    /// <summary>Which imagery provider a capture uses when the caller names none.</summary>
    public const string DefaultSourceKey = "defaultSource";

    /// <summary>How many pixels are cropped from each edge of a captured image when the caller names none.</summary>
    public const string DefaultCropPixelsKey = "defaultCropPixels";

    /// <summary>The zoom a capture uses when the caller names none — <c>auto</c> means the framing the view size implies.</summary>
    public const string DefaultZoomKey = "defaultZoom";

    /// <summary>Whether Google's web tone-mapped satellite output is corrected before it is delivered.</summary>
    public const string EnhanceGoogleColourKey = "enhanceGoogleColour";

    /// <summary>The zoom value that means "let the view size decide".</summary>
    public const string AutoZoomValue = "auto";

    public IReadOnlyList<ToolSettingDefinition> Definitions { get; } =
    [
        ToolSettingDefinition.Choice(
            MapSnapshotToolName,
            DefaultSourceKey,
            "Default imagery provider",
            "Which provider a snapshot uses when the request does not name one. Esri returns analysis-grade imagery " +
            "with no watermark; Google returns the same underlying photography tone-mapped for a browser, which the " +
            "colour correction below can partly undo.",
            MapCaptureSourceWire.Esri,
            [
                new(MapCaptureSourceWire.Esri, "Esri World Imagery"),
                new(MapCaptureSourceWire.Google, "Google Static Maps"),
            ]),

        ToolSettingDefinition.WholeNumber(
            MapSnapshotToolName,
            DefaultCropPixelsKey,
            "Default crop",
            "How many pixels are cropped from each of the four edges of a captured image when the request does not " +
            "say. A wider crop removes more of the provider's own framing — and, on Google's output, its logo and " +
            "attribution line.",
            32,
            0,
            200),

        ToolSettingDefinition.Choice(
            MapSnapshotToolName,
            DefaultZoomKey,
            "Default zoom",
            "The zoom a snapshot uses when the request does not say. Auto picks the level that fits the requested " +
            "view size. A fixed level zooms in further; each provider offers its own levels, and a level the other " +
            "one cannot serve is not offered.",
            AutoZoomValue,
            BuildZoomOptions(),
            scopedBy: DefaultSourceKey),

        ToolSettingDefinition.Boolean(
            MapSnapshotToolName,
            EnhanceGoogleColourKey,
            "Correct Google's colour",
            "Google tone-maps its satellite layer for the web, which lifts the blacks and drains the colour. This " +
            "corrects both on the way out. It cannot remove Google's logo or anything baked into the picture, and it " +
            "is never applied to Esri's imagery, which needs no correction.",
            true,
            scopedBy: DefaultSourceKey,
            scopeValue: MapCaptureSourceWire.Google),
    ];

    /// <summary>
    /// The zoom levels, each belonging to the provider that can serve it. Esri's export cannot be
    /// assembled past zoom 18; Google's reaches 21.
    /// </summary>
    private static List<ToolSettingOption> BuildZoomOptions()
    {
        var options = new List<ToolSettingOption>
        {
            new(ToolSettingCatalog.AutoZoomValue, "Auto (fit the view size)"),
        };

        for (var zoom = MapCaptureZoom.Minimum; zoom <= MapCaptureZoom.MaximumEsri; zoom++)
        {
            options.Add(new(
                zoom.ToString(System.Globalization.CultureInfo.InvariantCulture),
                $"Level {zoom}",
                MapCaptureSourceWire.Esri));
        }

        for (var zoom = MapCaptureZoom.MaximumEsri + 1; zoom <= MapCaptureZoom.Maximum; zoom++)
        {
            options.Add(new(
                zoom.ToString(System.Globalization.CultureInfo.InvariantCulture),
                $"Level {zoom}",
                MapCaptureSourceWire.Google));
        }

        return options;
    }

    public IReadOnlyList<ToolSettingDefinition> For(string toolName) =>
        [.. Definitions.Where(d => string.Equals(d.ToolName, toolName, StringComparison.Ordinal))];

    public ToolSettingDefinition? Find(string toolName, string key) =>
        Definitions.FirstOrDefault(d =>
            string.Equals(d.ToolName, toolName, StringComparison.Ordinal) &&
            string.Equals(d.Key, key, StringComparison.Ordinal));

    /// <summary>The tool names that declare at least one setting — the ones whose gear is enabled.</summary>
    public IReadOnlyList<string> ConfigurableTools =>
        [.. Definitions.Select(d => d.ToolName).Distinct(StringComparer.Ordinal)];

    /// <summary>Whether <paramref name="value"/> is a valid value for <paramref name="definition"/>, in its invariant form.</summary>
    public static bool IsValidValue(ToolSettingDefinition definition, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return definition.ValueType switch
        {
            ToolSettingValueType.Boolean => value is "true" or "false",
            ToolSettingValueType.Choice => definition.Choices.Any(o => string.Equals(o.Value, value, StringComparison.Ordinal)),
            ToolSettingValueType.Integer =>
                int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var number)
                && (definition.Minimum is null || number >= definition.Minimum)
                && (definition.Maximum is null || number <= definition.Maximum),
            _ => false,
        };
    }

    /// <summary>
    /// Whether the provider that will serve the capture can serve the zoom that was asked for.
    /// <para>
    /// This is the same rule the two providers really have — Esri's export cannot be assembled past
    /// zoom 18 — so a saved pair that breaks it would make every capture fail with a refusal. The
    /// rule is enforced when the settings are saved and again when they are read, so an
    /// incompatible pair can never reach a capture.
    /// </para>
    /// </summary>
    public static bool IsZoomServableBy(string source, string? zoom)
    {
        if (string.IsNullOrWhiteSpace(zoom) || string.Equals(zoom, AutoZoomValue, StringComparison.Ordinal))
        {
            return true;
        }

        if (!int.TryParse(zoom, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var level))
        {
            return false;
        }

        var maximum = string.Equals(source, MapCaptureSourceWire.Google, StringComparison.Ordinal)
            ? MapCaptureZoom.Maximum
            : MapCaptureZoom.MaximumEsri;

        return level >= MapCaptureZoom.Minimum && level <= maximum;
    }
}
