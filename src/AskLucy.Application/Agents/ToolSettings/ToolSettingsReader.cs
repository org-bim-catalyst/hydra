using System.Globalization;
using AskLucy.Application.Abstractions;
using AskLucy.Application.MapCaptures;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Agents.ToolSettings;

/// <summary>What a tool reads at run time: the saved value, or the catalogue default when none is saved.</summary>
public interface IToolSettingsReader
{
    /// <summary>
    /// The capture defaults an administrator has configured for the map snapshot tool. Never
    /// throws and never returns an invalid value: a setting that was never saved, or whose saved
    /// value is no longer valid, falls back to the catalogue's default.
    /// </summary>
    Task<MapCaptureDefaults> GetMapCaptureDefaultsAsync(CancellationToken cancellationToken = default);
}

public sealed class ToolSettingsReader(
    IToolSettingRepository settings,
    ToolSettingCatalog catalog,
    ILogger<ToolSettingsReader> logger) : IToolSettingsReader
{
    public async Task<MapCaptureDefaults> GetMapCaptureDefaultsAsync(CancellationToken cancellationToken = default)
    {
        var tool = ToolSettingCatalog.MapSnapshotToolName;

        IReadOnlyList<Domain.Agents.ToolSetting> rows;
        try
        {
            rows = await settings.ListByToolAsync(tool, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The settings are a convenience, not the capture itself: a capture that cannot read
            // them still has a documented default to fall back on. Failing the whole capture
            // because an optional configuration table could not be read would turn a missing
            // migration into a broken feature. The administrator still sees the failure — the
            // admin page's own settings read surfaces it — and the reason is logged here.
            ToolSettingsLog.SettingsUnavailable(logger, tool, exception);
            return MapCaptureDefaults.Default;
        }

        var stored = rows.ToDictionary(s => s.Key);

        return new MapCaptureDefaults(
            ReadSource(tool, stored),
            ReadCropPixels(tool, stored),
            ReadZoom(tool, stored),
            ReadBoolean(tool, stored, ToolSettingCatalog.EnhanceGoogleColourKey));
    }

    private string ValueOf(string tool, Dictionary<string, Domain.Agents.ToolSetting> stored, string key)
    {
        var definition = catalog.Find(tool, key)
            ?? throw new InvalidOperationException($"{tool} declares no setting '{key}'.");

        // A stored value that is no longer valid (the choice it named was withdrawn, the number
        // moved out of range) is ignored in favour of the default, rather than failing the capture.
        return stored.TryGetValue(key, out var row) && ToolSettingCatalog.IsValidValue(definition, row.Value)
            ? row.Value
            : definition.DefaultValue;
    }

    private MapCaptureSource ReadSource(string tool, Dictionary<string, Domain.Agents.ToolSetting> stored)
    {
        var value = ValueOf(tool, stored, ToolSettingCatalog.DefaultSourceKey);
        return MapCaptureSourceWire.TryParse(value, out var source) ? source : MapCaptureSource.Esri;
    }

    private int ReadCropPixels(string tool, Dictionary<string, Domain.Agents.ToolSetting> stored)
    {
        var value = ValueOf(tool, stored, ToolSettingCatalog.DefaultCropPixelsKey);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pixels) ? pixels : 32;
    }

    private int? ReadZoom(string tool, Dictionary<string, Domain.Agents.ToolSetting> stored)
    {
        var value = ValueOf(tool, stored, ToolSettingCatalog.DefaultZoomKey);
        if (string.Equals(value, ToolSettingCatalog.AutoZoomValue, StringComparison.Ordinal))
        {
            return null;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var zoom) ? zoom : null;
    }

    private bool ReadBoolean(string tool, Dictionary<string, Domain.Agents.ToolSetting> stored, string key) =>
        ValueOf(tool, stored, key) == "true";
}

internal static partial class ToolSettingsLog
{
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The settings for {Tool} could not be read, so the documented defaults apply")]
    public static partial void SettingsUnavailable(ILogger logger, string tool, Exception exception);
}
