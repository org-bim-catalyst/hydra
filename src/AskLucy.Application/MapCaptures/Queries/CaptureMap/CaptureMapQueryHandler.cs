using System.Diagnostics;
using System.Globalization;
using AskLucy.Application.Agents.ToolSettings;
using AskLucy.Application.Locations;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.MapCaptures.Queries.CaptureMap;

internal static partial class CaptureMapLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Map capture geocoding returned no match for '{Location}' (caller {Caller})")]
    public static partial void LocationNotFound(ILogger logger, string? location, string caller);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Map capture geocoding unavailable for '{Location}' (caller {Caller})")]
    public static partial void GeocodingUnavailable(ILogger logger, Exception exception, string? location, string caller);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Map capture refused overlay for '{Location}' (caller {Caller}): {Detail}")]
    public static partial void OverlayRefused(ILogger logger, string? location, string caller, string detail);

    [LoggerMessage(Level = LogLevel.Information, Message = "Map capture for {Caller}: '{Location}' ({Latitude}, {Longitude}) mode {Mode}, {OverlayCount} overlay(s), view {ViewSizeMetres} m")]
    public static partial void CaptureStarted(
        ILogger logger, string caller, string? location, double latitude, double longitude,
        MapCaptureMode mode, int overlayCount, int viewSizeMetres);

    [LoggerMessage(Level = LogLevel.Information, Message = "Map capture for {Caller} at '{Location}' finished in {ElapsedMs} ms (success: {Succeeded})")]
    public static partial void CaptureCompleted(
        ILogger logger, string caller, string location, long elapsedMs, bool succeeded);
}

/// <summary>
/// Resolves a capture location (coordinates win over free text) and delegates rendering. The
/// constructor deliberately has no storage port: this path is stateless (FR-014/SC-004).
/// </summary>
public sealed class CaptureMapQueryHandler(
    IGeocodingProvider geocodingProvider,
    IMapImageRenderer renderer,
    IToolSettingsReader settingsReader,
    IOptions<MapCaptureOptions> options,
    ILogger<CaptureMapQueryHandler> logger) : IRequestHandler<CaptureMapQuery, MapImageResult>
{
    public async Task<MapImageResult> Handle(CaptureMapQuery request, CancellationToken cancellationToken)
    {
        if (!MapCaptureWireFormat.TryParseMode(request.Mode, out var mode))
        {
            throw new ArgumentException("Mode was not validated.", nameof(request));
        }

        if (!MapCaptureWireFormat.TryParseOverlays(request.Overlays, out var overlays, out _))
        {
            throw new ArgumentException("Overlays were not validated.", nameof(request));
        }

        if (overlays.Contains(MapCaptureOverlay.SiteBoundary))
        {
            const string detail =
                "Overlay 'site-boundary' is not available on this path; use 'building-footprints'.";
            CaptureMapLog.OverlayRefused(logger, request.Location, request.CallerName, detail);
            return MapImageResult.Failure(MapCaptureFailureReason.OverlayUnavailable, detail);
        }

        double latitude;
        double longitude;
        string locationName;

        if (request.Latitude is { } suppliedLatitude && request.Longitude is { } suppliedLongitude)
        {
            latitude = suppliedLatitude;
            longitude = suppliedLongitude;
            locationName = string.Create(CultureInfo.InvariantCulture, $"{latitude},{longitude}");
        }
        else
        {
            IReadOnlyList<GeocodingCandidate> candidates;
            try
            {
                candidates = await geocodingProvider.SearchAsync(
                    request.Location ?? string.Empty, cancellationToken);
            }
            catch (GeocodingProviderUnavailableException exception)
            {
                CaptureMapLog.GeocodingUnavailable(logger, exception, request.Location, request.CallerName);
                return MapImageResult.Failure(
                    MapCaptureFailureReason.GeocodingUnavailable,
                    "The geocoding provider is temporarily unavailable; retry later.");
            }

            var best = candidates.OrderByDescending(candidate => candidate.Importance).FirstOrDefault();
            if (best is null)
            {
                CaptureMapLog.LocationNotFound(logger, request.Location, request.CallerName);
                return MapImageResult.Failure(
                    MapCaptureFailureReason.LocationNotFound,
                    $"No geocoding match was found for '{request.Location}'.");
            }

            latitude = best.Latitude;
            longitude = best.Longitude;
            locationName = best.LocationName;
        }

        var viewSizeMetres = request.ViewSizeMetres ?? options.Value.DefaultViewSizeMetres;

        // A request that names no source, crop or zoom takes the defaults an administrator
        // configured; the reader falls back to the catalogue's defaults for anything never saved.
        var defaults = await settingsReader.GetMapCaptureDefaultsAsync(cancellationToken);

        var source = request.Source is { } requestedSource
            ? MapCaptureSourceWire.TryParse(requestedSource, out var parsed) ? parsed : defaults.Source
            : defaults.Source;

        var cropPerEdge = request.CropPixels ?? defaults.CropPixels;
        var zoom = request.Zoom ?? defaults.Zoom;

        var imageRequest = new MapImageRequest(
            latitude, longitude, locationName, mode, overlays, viewSizeMetres, request.CallerName,
            source, cropPerEdge, zoom, defaults.EnhanceGoogleColour);

        CaptureMapLog.CaptureStarted(
            logger, request.CallerName, request.Location, latitude, longitude,
            mode, overlays.Count, viewSizeMetres);

        var stopwatch = Stopwatch.StartNew();
        var result = await renderer.RenderAsync(imageRequest, cancellationToken);
        stopwatch.Stop();

        CaptureMapLog.CaptureCompleted(
            logger, request.CallerName, locationName, stopwatch.ElapsedMilliseconds, result.IsSuccess);

        return result.IsSuccess ? result with { ResolvedLocation = locationName } : result;
    }
}
