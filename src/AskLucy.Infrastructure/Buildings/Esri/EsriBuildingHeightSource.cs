using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using AskLucy.Application.Buildings;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using AskLucy.Infrastructure.Boundaries;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Buildings.Esri;

internal static partial class EsriBuildingHeightSourceLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Esri building-height search failed for ({Latitude}, {Longitude})")]
    public static partial void SearchFailed(ILogger logger, Exception exception, double latitude, double longitude);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Esri building heights for ({Latitude}, {Longitude}): {Count} measured from {LeafCount} scene nodes ({PlaceholderCount} placeholder heights ignored)")]
    public static partial void SearchCompleted(ILogger logger, double latitude, double longitude, int count, int leafCount, int placeholderCount);
}

/// <summary>
/// specs/075 — <see cref="IBuildingHeightSource"/> over Esri's global 3D buildings scene layer
/// (I3S 1.7+, node pages, Draco geometry). Walks the node tree from the root, keeping only nodes
/// whose box comes within the radius, then reads each full-detail leaf's geometry (for where each
/// building is) and its height and source attributes.
///
/// <para>
/// Only heights the layer actually measured are returned. Buildings sourced from Vantor carry a
/// photogrammetric height and are trusted. Every other building's height comes from its
/// OpenStreetMap tags, and when OSM has none Esri fills in exactly 3.0 m — a placeholder, not a
/// measurement, so it is dropped rather than overriding the platform's own assumed height.
/// </para>
/// </summary>
internal sealed class EsriBuildingHeightSource(
    IHttpClientFactory httpClientFactory,
    II3sGeometryDecoder geometryDecoder,
    IMemoryCache cache,
    IOptions<EsriBuildingsOptions> options,
    IOptions<EsriOptions> esriOptions,
    IOptions<BuildingRetrievalOptions> retrievalOptions,
    ILogger<EsriBuildingHeightSource> logger) : IBuildingHeightSource
{
    public const string HttpClientName = "EsriSceneLayer";

    private const double PlaceholderHeightMetres = 3.0;

    /// <summary>
    /// Box distances are measured from a point on the ellipsoid, but a box's bottom can sit above
    /// it (ground level plus building bases). The slack keeps a node that is horizontally in range
    /// from being dropped for that vertical gap; the final radius filter uses each building's own
    /// position, so the slack never lets a far building through.
    /// </summary>
    private const double BoxDistanceSlackMetres = 100;

    private const int MaxConcurrentRequests = 8;

    private readonly EsriBuildingsOptions _options = options.Value;

    public async Task<IReadOnlyList<MeasuredBuildingHeight>> SearchAsync(GeoPoint center, int radiusMetres, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) return [];

        var cacheKey = $"esri-heights:{center.Latitude.ToString("F5", CultureInfo.InvariantCulture)},{center.Longitude.ToString("F5", CultureInfo.InvariantCulture)}:{radiusMetres}";
        if (cache.TryGetValue<IReadOnlyList<MeasuredBuildingHeight>>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            var result = await SearchUncachedAsync(center, radiusMetres, cancellationToken);
            cache.Set(cacheKey, result, retrievalOptions.Value.CacheTtl);
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException
            || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            EsriBuildingHeightSourceLog.SearchFailed(logger, ex, center.Latitude, center.Longitude);
            throw new BuildingProviderUnavailableException(
                $"Esri building heights unavailable for ({center.Latitude}, {center.Longitude}).", ex);
        }
    }

    private async Task<IReadOnlyList<MeasuredBuildingHeight>> SearchUncachedAsync(GeoPoint center, int radiusMetres, CancellationToken cancellationToken)
    {
        var httpClient = httpClientFactory.CreateClient(HttpClientName);
        using var throttle = new SemaphoreSlim(MaxConcurrentRequests);

        async Task<byte[]> GetAsync(string relativePath)
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{_options.SceneLayerUrl.TrimEnd('/')}{relativePath}");
                if (!string.IsNullOrWhiteSpace(esriOptions.Value.ApiKey))
                {
                    request.Headers.Add("X-Esri-Authorization", $"Bearer {esriOptions.Value.ApiKey}");
                }

                using var response = await httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                return Decompress(await response.Content.ReadAsByteArrayAsync(cancellationToken));
            }
            finally
            {
                throttle.Release();
            }
        }

        var layer = await cache.GetOrCreateAsync($"esri-layer:{_options.SceneLayerUrl}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _options.LayerCacheTtl;
            return I3sLayer.Parse(await GetAsync("?f=json"));
        }) ?? throw new InvalidDataException("The Esri scene layer description is empty.");

        var leaves = await FindLeavesAsync(layer, center, radiusMetres, GetAsync);

        var nodeResults = await Task.WhenAll(leaves.Select(leaf => ReadLeafAsync(layer, leaf, GetAsync)));

        var heights = new List<MeasuredBuildingHeight>();
        var placeholders = 0;
        foreach (var features in nodeResults)
        {
            foreach (var (location, height, source) in features)
            {
                if (!IsMeasured(height, source))
                {
                    placeholders++;
                    continue;
                }

                if (GeometryMath.DistanceMeters(center, location) <= radiusMetres)
                {
                    heights.Add(new MeasuredBuildingHeight(location, height));
                }
            }
        }

        EsriBuildingHeightSourceLog.SearchCompleted(logger, center.Latitude, center.Longitude, heights.Count, leaves.Count, placeholders);
        return heights;
    }

    /// <summary>
    /// A Vantor height is a measurement. Any other source's height came from OSM tags, where
    /// exactly 3.0 m is the layer's fill-in for "no height known".
    /// </summary>
    internal static bool IsMeasured(double heightMetres, string source) =>
        double.IsFinite(heightMetres) && heightMetres > 0
        && (source.Contains("Vantor", StringComparison.OrdinalIgnoreCase) || heightMetres != PlaceholderHeightMetres);

    /// <summary>
    /// Breadth-first, one tree level at a time, so every page a level needs is fetched together.
    /// Pages are cached under the layer's version: the levels near the root are the same for every
    /// request in the world.
    /// </summary>
    private async Task<List<I3sNode>> FindLeavesAsync(
        I3sLayer layer, GeoPoint center, int radiusMetres, Func<string, Task<byte[]>> getAsync)
    {
        var leaves = new List<I3sNode>();
        var frontier = new List<int> { 0 };
        while (frontier.Count > 0)
        {
            var pageIndices = frontier.Select(i => i / layer.NodesPerPage).Distinct().ToList();
            var pages = await Task.WhenAll(pageIndices.Select(p => cache.GetOrCreateAsync(
                $"esri-nodepage:{_options.SceneLayerUrl}:{layer.Version}:{p}",
                async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = _options.NodePageCacheTtl;
                    return I3sNode.ParsePage(await getAsync($"/nodepages/{p}"));
                })));
            var pageByIndex = pageIndices.Zip(pages).ToDictionary(p => p.First, p => p.Second
                ?? throw new InvalidDataException($"Esri node page {p.First} is empty."));

            var next = new List<int>();
            foreach (var index in frontier)
            {
                var page = pageByIndex[index / layer.NodesPerPage];
                var offset = index % layer.NodesPerPage;
                if (offset >= page.Count)
                    throw new InvalidDataException($"Esri node {index} is missing from its page.");

                var node = page[offset];
                if (node.Box.DistanceMetres(center) > radiusMetres + BoxDistanceSlackMetres) continue;

                if (node.Children.Count > 0) next.AddRange(node.Children);
                else if (node.Mesh is not null) leaves.Add(node);
            }

            frontier = next;
        }

        return leaves;
    }

    private async Task<List<(GeoPoint Location, double Height, string Source)>> ReadLeafAsync(
        I3sLayer layer, I3sNode leaf, Func<string, Task<byte[]>> getAsync)
    {
        var mesh = leaf.Mesh!.Value;
        var geometryTask = getAsync($"/nodes/{mesh.GeometryResource}/geometries/0");
        var heightTask = getAsync($"/nodes/{mesh.AttributeResource}/attributes/{layer.HeightKey}/0");
        var sourceTask = getAsync($"/nodes/{mesh.AttributeResource}/attributes/{layer.SourceKey}/0");
        await Task.WhenAll(geometryTask, heightTask, sourceTask);

        var heights = I3sAttributeReader.ReadNumbers(await heightTask, layer.HeightValueType);
        var sources = I3sAttributeReader.ReadStrings(await sourceTask);
        if (sources.Length != heights.Length)
            throw new InvalidDataException($"Esri node {mesh.AttributeResource} has {heights.Length} heights but {sources.Length} sources.");

        var centres = geometryDecoder.DecodeFeatureCentres(
            await geometryTask, leaf.Box.CenterLongitude, leaf.Box.CenterLatitude, heights.Length);

        var features = new List<(GeoPoint, double, string)>(heights.Length);
        for (var i = 0; i < heights.Length && i < centres.Count; i++)
        {
            if (centres[i] is { } c)
            {
                features.Add((new GeoPoint(c.Latitude, c.Longitude), heights[i], sources[i]));
            }
        }

        return features;
    }

    /// <summary>The service gzips resources whether or not the response says so.</summary>
    private static byte[] Decompress(byte[] body)
    {
        if (body.Length < 2 || body[0] != 0x1F || body[1] != 0x8B) return body;

        using var output = new MemoryStream();
        using (var gzip = new GZipStream(new MemoryStream(body), CompressionMode.Decompress))
        {
            gzip.CopyTo(output);
        }

        return output.ToArray();
    }
}

/// <summary>The parts of an I3S layer description this source reads.</summary>
internal sealed record I3sLayer(int NodesPerPage, string Version, string HeightKey, string HeightValueType, string SourceKey)
{
    public static I3sLayer Parse(byte[] json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.TryGetProperty("error", out var error))
            throw new InvalidDataException($"The Esri scene layer returned an error: {error}");

        var nodesPerPage = root.GetProperty("nodePages").GetProperty("nodesPerPage").GetInt32();
        var version = root.TryGetProperty("version", out var v) ? v.GetString() ?? string.Empty : string.Empty;

        (string Key, string ValueType)? height = null, source = null;
        foreach (var attribute in root.GetProperty("attributeStorageInfo").EnumerateArray())
        {
            var name = attribute.GetProperty("name").GetString();
            var key = attribute.GetProperty("key").GetString()!;
            var valueType = attribute.TryGetProperty("attributeValues", out var values)
                && values.TryGetProperty("valueType", out var type) ? type.GetString() ?? string.Empty : string.Empty;
            if (string.Equals(name, "height", StringComparison.OrdinalIgnoreCase)) height = (key, valueType);
            if (string.Equals(name, "source", StringComparison.OrdinalIgnoreCase)) source = (key, valueType);
        }

        if (height is null || source is null)
            throw new InvalidDataException("The Esri scene layer no longer has 'height' and 'source' attributes.");

        return new I3sLayer(nodesPerPage, version, height.Value.Key, height.Value.ValueType, source.Value.Key);
    }
}

/// <summary>One entry of an I3S node page.</summary>
internal sealed record I3sNode(I3sOrientedBoundingBox Box, IReadOnlyList<int> Children, I3sNodeMesh? Mesh)
{
    public static IReadOnlyList<I3sNode> ParsePage(byte[] json)
    {
        using var document = JsonDocument.Parse(json);
        var nodes = new List<I3sNode>();
        foreach (var node in document.RootElement.GetProperty("nodes").EnumerateArray())
        {
            var obb = node.GetProperty("obb");
            var c = obb.GetProperty("center");
            var h = obb.GetProperty("halfSize");
            var q = obb.GetProperty("quaternion");
            var box = new I3sOrientedBoundingBox(
                c[0].GetDouble(), c[1].GetDouble(), c[2].GetDouble(),
                h[0].GetDouble(), h[1].GetDouble(), h[2].GetDouble(),
                q[0].GetDouble(), q[1].GetDouble(), q[2].GetDouble(), q[3].GetDouble());

            var children = node.TryGetProperty("children", out var kids)
                ? kids.EnumerateArray().Select(k => k.GetInt32()).ToList()
                : [];

            I3sNodeMesh? mesh = null;
            if (node.TryGetProperty("mesh", out var m)
                && m.TryGetProperty("geometry", out var geometry)
                && m.TryGetProperty("attribute", out var attribute))
            {
                mesh = new I3sNodeMesh(
                    geometry.GetProperty("resource").GetInt32(),
                    attribute.GetProperty("resource").GetInt32());
            }

            nodes.Add(new I3sNode(box, children, mesh));
        }

        return nodes;
    }
}

internal readonly record struct I3sNodeMesh(int GeometryResource, int AttributeResource);
