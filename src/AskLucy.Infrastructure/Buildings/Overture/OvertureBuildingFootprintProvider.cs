using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using AskLucy.Application.Buildings;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Buildings.Overture;

internal static partial class OvertureBuildingFootprintProviderLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Overture building search failed for ({Latitude}, {Longitude})")]
    public static partial void SearchFailed(ILogger logger, Exception exception, double latitude, double longitude);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Overture release {Release} has no readable buildings archive; trying an older release")]
    public static partial void ReleaseUnreadable(ILogger logger, Exception exception, string release);

    [LoggerMessage(Level = LogLevel.Information, Message = "Using Overture buildings release {Release}")]
    public static partial void ReleaseSelected(ILogger logger, string release);
}

/// <summary>
/// specs/075 — <see cref="IBuildingFootprintProvider"/> over the Overture Maps Foundation's
/// buildings theme, read from the Foundation's public PMTiles archive with HTTP range requests (no
/// DuckDB, no native code). Overture merges OpenStreetMap with Microsoft's and Google's
/// machine-detected footprints, so it covers the residential areas OSM alone leaves empty.
///
/// <para>
/// Tiles clip buildings at their edges (with a small overlap). Each tile's shapes are clipped
/// again to the tile's exact bounds, so a building that crosses an edge comes back as adjacent
/// pieces that together form the whole building, never as overlapping duplicates. Pieces keep the
/// building's height, so their shadows are the building's shadow.
/// </para>
///
/// <para>
/// Heights: Overture's own <c>height</c> is recorded (OSM or the Foundation's height sources), so
/// it is <see cref="BuildingHeightProvenance.Known"/>, as is the tallest of a building's parts;
/// <c>num_floors</c> × 3 m and the 9 m default are assumed — the same rule
/// <c>OverpassBuildingFootprintProvider</c> applies to OSM tags.
/// </para>
/// </summary>
internal sealed class OvertureBuildingFootprintProvider(
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    IOptions<OvertureBuildingsOptions> options,
    IOptions<BuildingRetrievalOptions> retrievalOptions,
    ILogger<OvertureBuildingFootprintProvider> logger) : IBuildingFootprintProvider
{
    public const string HttpClientName = "OvertureTiles";

    private const string BuildingLayer = "building";
    private const string BuildingPartLayer = "building_part";
    private const int MaxDirectoryDepth = 4;

    private readonly OvertureBuildingsOptions _options = options.Value;
    private readonly BuildingRetrievalOptions _retrievalOptions = retrievalOptions.Value;

    public async Task<BuildingFootprintResult> SearchAsync(GeoPoint center, int radiusMetres, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return new BuildingFootprintResult([], false, 0, radiusMetres, BuildingFootprintSource.None);
        }

        var cacheKey = $"overture-buildings:{center.Latitude.ToString("F5", CultureInfo.InvariantCulture)},{center.Longitude.ToString("F5", CultureInfo.InvariantCulture)}:{radiusMetres}";
        if (cache.TryGetValue<BuildingFootprintResult>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            var result = await SearchUncachedAsync(center, radiusMetres, cancellationToken);
            cache.Set(cacheKey, result, _retrievalOptions.CacheTtl);
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or XmlException or JsonException
            || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            OvertureBuildingFootprintProviderLog.SearchFailed(logger, ex, center.Latitude, center.Longitude);
            throw new BuildingProviderUnavailableException(
                $"Overture building search unavailable for ({center.Latitude}, {center.Longitude}).", ex);
        }
    }

    private async Task<BuildingFootprintResult> SearchUncachedAsync(GeoPoint center, int radiusMetres, CancellationToken cancellationToken)
    {
        var httpClient = httpClientFactory.CreateClient(HttpClientName);
        var archive = await cache.GetOrCreateAsync($"overture-archive:{_options.BucketUrl}:{_options.Release}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _options.ArchiveCacheTtl;
            return await OpenArchiveAsync(httpClient, cancellationToken);
        }) ?? throw new InvalidDataException("No Overture buildings archive.");

        var zoom = archive.Header.MaxZoom;
        var tiles = CoveringTiles(center, radiusMetres, zoom);
        var tileBytes = await Task.WhenAll(tiles.Select(t => ReadTileAsync(httpClient, archive, zoom, t.X, t.Y, cancellationToken)));

        var pieces = new List<Piece>();
        var partHeights = new Dictionary<string, double>();
        for (var i = 0; i < tiles.Count; i++)
        {
            if (tileBytes[i] is not { } bytes) continue;
            var layers = MvtTile.Parse(bytes);
            foreach (var part in layers.Where(l => l.Name == BuildingPartLayer).SelectMany(l => l.Features))
            {
                if (Text(part, "building_id") is { } buildingId && Number(part, "height") is > 0 and var height)
                {
                    partHeights[buildingId] = Math.Max(partHeights.GetValueOrDefault(buildingId), height);
                }
            }

            foreach (var layer in layers.Where(l => l.Name == BuildingLayer))
            {
                pieces.AddRange(ToPieces(layer, zoom, tiles[i].X, tiles[i].Y));
            }
        }

        return BuildResult(pieces, partHeights, center, radiusMetres);
    }

    private BuildingFootprintResult BuildResult(
        List<Piece> pieces, Dictionary<string, double> partHeights, GeoPoint center, int radiusMetres)
    {
        var usable = new List<(Piece Piece, double Distance)>();
        foreach (var piece in pieces)
        {
            if (GeometryMath.AreaSquareMeters(piece.Ring) < 1.0)
            {
                // Slivers left where a tile's overlap was clipped off, or a building touching the
                // tile edge only along a line — not a footprint of their own.
                continue;
            }

            // Kept whole when any part is in range, as the other providers do: truncating a
            // footprint at the radius would cast a shadow the real building does not. Buildings
            // wholly out of range are not "excluded" (FR-013 counts unusable footprints); a tile
            // is simply larger than the search area.
            if (!GeometryMath.Contains(piece.Ring, center) && piece.Ring.All(p => GeometryMath.DistanceMeters(p, center) > radiusMetres))
            {
                continue;
            }

            usable.Add((piece, GeometryMath.DistanceMeters(GeometryMath.Centroid(piece.Ring), center)));
        }

        usable.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        var rings = usable.Select(u => u.Piece.Ring).ToList();
        var siteBuildingIndex = SiteBuildingLocator.FindIndex(rings, center);
        var siteBuildingId = siteBuildingIndex >= 0 ? usable[siteBuildingIndex].Piece.BuildingId : null;

        var limited = usable.Count > _retrievalOptions.MaxBuildingCount;
        var bounded = limited ? usable.Take(_retrievalOptions.MaxBuildingCount).ToList() : usable;
        var pieceCounts = bounded.GroupBy(u => u.Piece.BuildingId).ToDictionary(g => g.Key, g => g.Count());
        var pieceNumbers = new Dictionary<string, int>();

        var buildings = new List<BuildingFootprint>(bounded.Count);
        foreach (var (piece, _) in bounded)
        {
            var id = $"overture_{piece.BuildingId}";
            if (pieceCounts[piece.BuildingId] > 1)
            {
                var number = pieceNumbers[piece.BuildingId] = pieceNumbers.GetValueOrDefault(piece.BuildingId) + 1;
                id = $"{id}_{number}";
            }

            var (heightMetres, provenance) = ResolveHeight(piece, partHeights);
            var closedRing = piece.Ring.Append(piece.Ring[0]).ToList();
            buildings.Add(new BuildingFootprint(
                Id: id,
                Ring: closedRing,
                HeightMetres: heightMetres,
                HeightProvenance: provenance,
                Name: piece.Name,
                // A building split across tiles is still one building: every piece is the site.
                IsSiteBuilding: piece.BuildingId == siteBuildingId));
        }

        return new BuildingFootprintResult(buildings, limited, ExcludedCount: 0, radiusMetres, BuildingFootprintSource.Overture);
    }

    internal static (double HeightMetres, BuildingHeightProvenance Provenance) ResolveHeight(Piece piece, IReadOnlyDictionary<string, double> partHeights)
    {
        if (piece.Height is > 0 and var height) return (height, BuildingHeightProvenance.Known);
        if (partHeights.TryGetValue(piece.BuildingId, out var partHeight)) return (partHeight, BuildingHeightProvenance.Known);
        if (piece.Floors is > 0 and var floors) return (floors * AssumedBuildingHeight.MetresPerStorey, BuildingHeightProvenance.Assumed);
        return (AssumedBuildingHeight.DefaultMetres, BuildingHeightProvenance.Assumed);
    }

    /// <summary>Exterior rings only — holes are courtyards, which cast no shadow of their own.</summary>
    internal static IEnumerable<Piece> ToPieces(MvtLayer layer, int zoom, int tileX, int tileY)
    {
        foreach (var feature in layer.Features)
        {
            if (feature.Properties.GetValueOrDefault("is_underground") is true) continue;
            if (Text(feature, "id") is not { } id) continue;

            var height = Number(feature, "height");
            var floors = Number(feature, "num_floors");
            var name = Text(feature, "@name") ?? string.Empty;
            foreach (var ring in feature.Rings.Where(r => r.SignedArea() > 0))
            {
                var clipped = ClipToTile(ring.Points, layer.Extent);
                if (clipped.Count < 3) continue;

                var geo = clipped.Select(p => ToGeoPoint(p.X, p.Y, layer.Extent, zoom, tileX, tileY)).ToList();
                yield return new Piece(id, geo, height, floors, name);
            }
        }
    }

    /// <summary>Sutherland–Hodgman against the square 0..extent; the input ring is convex-or-not, the clip square is convex.</summary>
    internal static List<(double X, double Y)> ClipToTile(IReadOnlyList<(double X, double Y)> ring, int extent)
    {
        var output = ring.ToList();
        foreach (var (inside, intersect) in new (Func<(double X, double Y), bool>, Func<(double X, double Y), (double X, double Y), (double X, double Y)>)[]
        {
            (p => p.X >= 0, (a, b) => (0, a.Y + ((b.Y - a.Y) * (0 - a.X) / (b.X - a.X)))),
            (p => p.X <= extent, (a, b) => (extent, a.Y + ((b.Y - a.Y) * (extent - a.X) / (b.X - a.X)))),
            (p => p.Y >= 0, (a, b) => (a.X + ((b.X - a.X) * (0 - a.Y) / (b.Y - a.Y)), 0)),
            (p => p.Y <= extent, (a, b) => (a.X + ((b.X - a.X) * (extent - a.Y) / (b.Y - a.Y)), extent)),
        })
        {
            if (output.Count == 0) break;
            var input = output;
            output = [];
            for (var i = 0; i < input.Count; i++)
            {
                var current = input[i];
                var previous = input[(i + input.Count - 1) % input.Count];
                if (inside(current))
                {
                    if (!inside(previous)) output.Add(intersect(previous, current));
                    output.Add(current);
                }
                else if (inside(previous))
                {
                    output.Add(intersect(previous, current));
                }
            }
        }

        return output;
    }

    internal static GeoPoint ToGeoPoint(double x, double y, int extent, int zoom, int tileX, int tileY)
    {
        var n = Math.Pow(2, zoom);
        var longitude = ((tileX + (x / extent)) / n * 360.0) - 180.0;
        var latitude = Math.Atan(Math.Sinh(Math.PI * (1 - (2 * (tileY + (y / extent)) / n)))) * 180.0 / Math.PI;
        return new GeoPoint(latitude, longitude);
    }

    internal static List<(int X, int Y)> CoveringTiles(GeoPoint center, int radiusMetres, int zoom)
    {
        const double MetresPerDegreeLatitude = 111_320.0;
        var latitudeDelta = radiusMetres / MetresPerDegreeLatitude;
        var longitudeDelta = radiusMetres / (MetresPerDegreeLatitude * Math.Cos(center.Latitude * Math.PI / 180.0));
        var (minX, maxY) = TileOf(center.Latitude - latitudeDelta, center.Longitude - longitudeDelta, zoom);
        var (maxX, minY) = TileOf(center.Latitude + latitudeDelta, center.Longitude + longitudeDelta, zoom);

        var tiles = new List<(int, int)>();
        for (var x = minX; x <= maxX; x++)
        {
            for (var y = minY; y <= maxY; y++) tiles.Add((x, y));
        }

        return tiles;
    }

    private static (int X, int Y) TileOf(double latitude, double longitude, int zoom)
    {
        var n = 1 << zoom;
        var phi = latitude * Math.PI / 180.0;
        var x = (int)Math.Floor((longitude + 180.0) / 360.0 * n);
        var y = (int)Math.Floor((1 - (Math.Log(Math.Tan(phi) + (1 / Math.Cos(phi))) / Math.PI)) / 2 * n);
        return (Math.Clamp(x, 0, n - 1), Math.Clamp(y, 0, n - 1));
    }

    /// <summary>Null when the archive has no tile there.</summary>
    private async Task<byte[]?> ReadTileAsync(HttpClient httpClient, OvertureArchive archive, int zoom, int x, int y, CancellationToken cancellationToken)
    {
        var tileId = PmTiles.TileId(zoom, x, y);
        var directory = archive.RootDirectory;
        for (var depth = 0; depth < MaxDirectoryDepth; depth++)
        {
            if (PmTiles.Find(directory, tileId) is not { } entry) return null;
            if (entry.RunLength > 0)
            {
                var tile = await ReadRangeAsync(httpClient, archive.Url, archive.Header.TileDataOffset + entry.Offset, entry.Length, cancellationToken);
                return PmTiles.Decompress(tile, archive.Header.TileCompression);
            }

            var leafOffset = archive.Header.LeafDirectoriesOffset + entry.Offset;
            directory = await cache.GetOrCreateAsync($"overture-leaf:{archive.Url}:{leafOffset}", async cacheEntry =>
            {
                cacheEntry.AbsoluteExpirationRelativeToNow = _options.ArchiveCacheTtl;
                var bytes = await ReadRangeAsync(httpClient, archive.Url, leafOffset, entry.Length, cancellationToken);
                return PmTiles.ParseDirectory(PmTiles.Decompress(bytes, archive.Header.InternalCompression));
            }) ?? throw new InvalidDataException("Empty PMTiles leaf directory.");
        }

        throw new InvalidDataException("PMTiles directories nest deeper than the format allows.");
    }

    /// <summary>
    /// The newest release whose buildings archive opens — a release folder can appear in the
    /// bucket before its files finish uploading.
    /// </summary>
    private async Task<OvertureArchive> OpenArchiveAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        var releases = string.IsNullOrWhiteSpace(_options.Release)
            ? await ListReleasesAsync(httpClient, cancellationToken)
            : [_options.Release];
        if (releases.Count == 0) throw new InvalidDataException("The Overture bucket lists no tile releases.");

        const int MaxReleasesTried = 3;
        for (var i = 0; ; i++)
        {
            var url = $"{_options.BucketUrl.TrimEnd('/')}/tiles/{releases[i]}/buildings.pmtiles";
            try
            {
                var header = PmTilesHeader.Parse(await ReadRangeAsync(httpClient, url, 0, PmTilesHeader.Length, cancellationToken));
                if (header.TileType != PmTilesHeader.MapboxVectorTileType)
                    throw new InvalidDataException($"Overture release {releases[i]} does not hold vector tiles.");

                var rootBytes = await ReadRangeAsync(httpClient, url, header.RootDirectoryOffset, (int)header.RootDirectoryLength, cancellationToken);
                var root = PmTiles.ParseDirectory(PmTiles.Decompress(rootBytes, header.InternalCompression));
                OvertureBuildingFootprintProviderLog.ReleaseSelected(logger, releases[i]);
                return new OvertureArchive(url, header, root);
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException && i + 1 < Math.Min(releases.Count, MaxReleasesTried))
            {
                OvertureBuildingFootprintProviderLog.ReleaseUnreadable(logger, ex, releases[i]);
            }
        }
    }

    /// <summary>S3 ListObjectsV2 of the "tiles/" folder; release names are dates, so they sort newest-last as text.</summary>
    private async Task<List<string>> ListReleasesAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        var url = $"{_options.BucketUrl.TrimEnd('/')}/?list-type=2&prefix=tiles/&delimiter=/";
        using var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var document = XDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.Descendants()
            .Where(e => e.Name.LocalName == "Prefix" && e.Parent?.Name.LocalName == "CommonPrefixes")
            .Select(e => e.Value.TrimEnd('/').Split('/')[^1])
            .Where(r => r.Length > 0)
            .OrderByDescending(r => r, StringComparer.Ordinal)
            .ToList();
    }

    private static async Task<byte[]> ReadRangeAsync(HttpClient httpClient, string url, long offset, int length, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(offset, offset + length - 1);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.StatusCode != HttpStatusCode.PartialContent)
            throw new InvalidDataException("The Overture archive host ignored the byte range.");

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return bytes.Length == length ? bytes : throw new InvalidDataException("The Overture archive returned a short range.");
    }

    private static string? Text(MvtFeature feature, string key) => feature.Properties.GetValueOrDefault(key) as string;

    private static double? Number(MvtFeature feature, string key) => feature.Properties.GetValueOrDefault(key) switch
    {
        double d => d,
        long l => l,
        _ => null,
    };

    internal sealed record Piece(string BuildingId, IReadOnlyList<GeoPoint> Ring, double? Height, double? Floors, string Name);

    private sealed record OvertureArchive(string Url, PmTilesHeader Header, IReadOnlyList<PmTilesEntry> RootDirectory);
}
