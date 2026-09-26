using System.Net;
using AskLucy.Application.Buildings;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using AskLucy.Infrastructure.Buildings;
using AskLucy.Infrastructure.Buildings.Overture;
using AskLucy.Infrastructure.Tests.Ai;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Buildings.Overture;

/// <summary>
/// specs/075 — <see cref="OvertureBuildingFootprintProvider"/> against a synthetic PMTiles archive
/// served the way the Overture bucket serves it: release discovery, directory walking, tile-edge
/// clipping, the height rule, the site building, caching, and the typed unavailable-exception.
/// </summary>
public sealed class OvertureBuildingFootprintProviderTests
{
    private const int Zoom = 14;

    // The z14 tile holding Al Safa Park, and the point at its centre (tile units 2048, 2048).
    private static readonly (int X, int Y) SiteTile = TileOf(25.1560, 55.2218);
    private static readonly GeoPoint Center = At(SiteTile, 2048, 2048);

    private static (int X, int Y) TileOf(double latitude, double longitude) =>
        OvertureBuildingFootprintProvider.CoveringTiles(new GeoPoint(latitude, longitude), 0, Zoom).Single();

    private static GeoPoint At((int X, int Y) tile, double x, double y) =>
        OvertureBuildingFootprintProvider.ToGeoPoint(x, y, 4096, Zoom, tile.X, tile.Y);

    private static IReadOnlyList<(int X, int Y)> Square(int x, int y, int size) =>
        [(x, y), (x + size, y), (x + size, y + size), (x, y + size)];

    private static TestBuilding Building(string id, IReadOnlyList<(int X, int Y)> ring, params (string Key, object Value)[] properties) =>
        new([ring], new Dictionary<string, object>(properties.Select(p => KeyValuePair.Create(p.Key, p.Value))) { ["id"] = id });

    private static (OvertureBuildingFootprintProvider Provider, OvertureTestBucket Bucket) Create(
        Action<OvertureTestBucket> arrange, OvertureBuildingsOptions? options = null, BuildingRetrievalOptions? retrieval = null)
    {
        var bucket = new OvertureTestBucket();
        arrange(bucket);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(OvertureBuildingFootprintProvider.HttpClientName)
            .Returns(_ => new HttpClient(new StubHttpMessageHandler(bucket.Respond)));

        var provider = new OvertureBuildingFootprintProvider(
            factory,
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(options ?? new OvertureBuildingsOptions { BucketUrl = OvertureTestBucket.BucketUrl }),
            Options.Create(retrieval ?? new BuildingRetrievalOptions()),
            NullLogger<OvertureBuildingFootprintProvider>.Instance);
        return (provider, bucket);
    }

    private static Dictionary<ulong, byte[]> SiteTileWith(IReadOnlyList<TestBuilding> buildings, IReadOnlyList<TestBuilding>? parts = null) =>
        new()
        {
            [PmTiles.TileId(Zoom, SiteTile.X, SiteTile.Y)] = VectorTileWriter.Tile(("building", buildings), ("building_part", parts ?? [])),
        };

    [Fact]
    public async Task SearchAsync_ShouldApplyTheHeightRule_AndMarkTheSiteBuilding()
    {
        var (provider, _) = Create(bucket => bucket.AddRelease("2026-09-23.1", SiteTileWith(
            [
                Building("site", Square(2040, 2040, 16), ("height", 30.0)),
                Building("floors", Square(2100, 2040, 16), ("num_floors", 4)),
                Building("bare", Square(2140, 2040, 16)),
                Building("tower", Square(2180, 2040, 16)),
            ],
            parts: [Building("part", Square(2180, 2040, 8), ("building_id", "tower"), ("height", 45.0))])));

        var result = await provider.SearchAsync(Center, 200, TestContext.Current.CancellationToken);

        result.Source.Should().Be(BuildingFootprintSource.Overture);
        var byId = result.Buildings.ToDictionary(b => b.Id);
        byId["overture_site"].Should().Match<BuildingFootprint>(b => b.HeightMetres == 30 && b.HeightProvenance == BuildingHeightProvenance.Known && b.IsSiteBuilding);
        byId["overture_floors"].Should().Match<BuildingFootprint>(b => b.HeightMetres == 12 && b.HeightProvenance == BuildingHeightProvenance.Assumed);
        byId["overture_bare"].Should().Match<BuildingFootprint>(b => b.HeightMetres == 9 && b.HeightProvenance == BuildingHeightProvenance.Assumed);
        byId["overture_tower"].Should().Match<BuildingFootprint>(b => b.HeightMetres == 45 && b.HeightProvenance == BuildingHeightProvenance.Known,
            "the tallest recorded part is the building's height when the building itself has none");
        result.Buildings.Should().OnlyContain(b => b.Ring.Count == 5 && b.Ring[0] == b.Ring[b.Ring.Count - 1], "rings are closed like the other providers'");
        result.Buildings[0].Id.Should().Be("overture_site", "buildings are ordered nearest first");
    }

    [Fact]
    public async Task SearchAsync_ShouldPlaceFootprints_WhereTheTileSaysTheyAre()
    {
        var (provider, _) = Create(bucket => bucket.AddRelease("r1", SiteTileWith([Building("site", Square(2040, 2040, 16))])));

        var ring = (await provider.SearchAsync(Center, 200, TestContext.Current.CancellationToken)).Buildings.Single().Ring;

        GeometryMath.Contains(ring, Center).Should().BeTrue();
        GeometryMath.DistanceMeters(ring[0], At(SiteTile, 2040, 2040)).Should().BeLessThan(0.01);
    }

    [Fact]
    public async Task SearchAsync_ShouldSkipUndergroundBuildings_HolesAndBuildingsOutOfRange()
    {
        var courtyard = new TestBuilding(
            [Square(2040, 2040, 40), [(2050, 2050), (2050, 2070), (2070, 2070), (2070, 2050)]],
            new Dictionary<string, object> { ["id"] = "courtyard" });
        var (provider, _) = Create(bucket => bucket.AddRelease("r1", SiteTileWith(
        [
            courtyard,
            Building("car-park", Square(2100, 2100, 30), ("is_underground", true)),
            Building("far", Square(3500, 3500, 20)),
        ])));

        var result = await provider.SearchAsync(Center, 200, TestContext.Current.CancellationToken);

        result.Buildings.Should().ContainSingle().Which.Id.Should().Be("overture_courtyard", "the hole is not a building of its own");
        result.ExcludedCount.Should().Be(0, "buildings merely outside the radius are not unusable footprints");
    }

    [Fact]
    public async Task SearchAsync_ShouldSplitABuildingCrossingATileEdge_IntoAdjacentPieces()
    {
        // The site sits on the boundary between two tiles; each tile carries the building clipped
        // with its usual overlap, which must be trimmed back to the tile's own bounds.
        var east = (SiteTile.X + 1, SiteTile.Y);
        var center = At(SiteTile, 4090, 2060);
        var (provider, _) = Create(bucket => bucket.AddRelease("r1", new Dictionary<ulong, byte[]>
        {
            [PmTiles.TileId(Zoom, SiteTile.X, SiteTile.Y)] = VectorTileWriter.Tile(("building", [Building("hall", Square(4080, 2040, 40), ("height", 20.0))])),
            [PmTiles.TileId(Zoom, east.Item1, east.Item2)] = VectorTileWriter.Tile(("building", [Building("hall", Square(-16, 2040, 40), ("height", 20.0))])),
        }));

        var result = await provider.SearchAsync(center, 200, TestContext.Current.CancellationToken);

        result.Buildings.Select(b => b.Id).Should().BeEquivalentTo("overture_hall_1", "overture_hall_2");
        result.Buildings.Should().OnlyContain(b => b.HeightMetres == 20 && b.IsSiteBuilding,
            "every piece is the same building: same height, and all of it is the site");
        var pieceAreas = result.Buildings.Sum(b => GeometryMath.AreaSquareMeters(b.Ring.Take(4).ToList()));
        var wholeArea = GeometryMath.AreaSquareMeters(
            [At(SiteTile, 4080, 2040), At(SiteTile, 4080 + 40, 2040), At(SiteTile, 4080 + 40, 2080), At(SiteTile, 4080, 2080)]);
        pieceAreas.Should().BeApproximately(wholeArea, wholeArea * 0.01, "the pieces tile the building exactly — no overlap, no gap");
    }

    [Fact]
    public async Task SearchAsync_ShouldReadTilesThroughALeafDirectory()
    {
        var (provider, _) = Create(bucket => bucket.AddRelease("r1", SiteTileWith([Building("site", Square(2040, 2040, 16))]), useLeafDirectory: true));

        (await provider.SearchAsync(Center, 200, TestContext.Current.CancellationToken)).Buildings.Should().ContainSingle();
    }

    [Fact]
    public async Task SearchAsync_ShouldUseTheNewestRelease_AndSkipOneWhoseArchiveIsMissing()
    {
        var (provider, bucket) = Create(bucket =>
        {
            bucket.AddRelease("2026-08-19.0", SiteTileWith([Building("old", Square(2040, 2040, 16))]));
            bucket.AddRelease("2026-09-23.0", SiteTileWith([Building("new", Square(2040, 2040, 16))]));
            bucket.AddEmptyRelease("2026-09-30.0");
        });

        var result = await provider.SearchAsync(Center, 200, TestContext.Current.CancellationToken);

        result.Buildings.Single().Id.Should().Be("overture_new");
        bucket.Requests.Should().Contain(r => r.Contains("2026-09-30.0"), "the newest listed release is tried first");
    }

    [Fact]
    public async Task SearchAsync_ShouldReturnAnEmptyOvertureResult_WhereTheArchiveHasNoTile()
    {
        var (provider, _) = Create(bucket => bucket.AddRelease("r1", new Dictionary<ulong, byte[]>
        {
            [PmTiles.TileId(Zoom, 0, 0)] = VectorTileWriter.Tile(("building", [])),
        }));

        var result = await provider.SearchAsync(Center, 200, TestContext.Current.CancellationToken);

        result.Buildings.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_ShouldCapTheBuildingCount_AndSayItWasLimited()
    {
        var (provider, _) = Create(
            bucket => bucket.AddRelease("r1", SiteTileWith(
                Enumerable.Range(0, 5).Select(i => Building($"b{i}", Square(2040 + (i * 20), 2040, 16))).ToList())),
            retrieval: new BuildingRetrievalOptions { MaxBuildingCount = 3 });

        var result = await provider.SearchAsync(Center, 200, TestContext.Current.CancellationToken);

        result.Buildings.Should().HaveCount(3);
        result.Limited.Should().BeTrue();
    }

    [Fact]
    public async Task SearchAsync_ShouldCacheTheResult()
    {
        var (provider, bucket) = Create(bucket => bucket.AddRelease("r1", SiteTileWith([Building("site", Square(2040, 2040, 16))])));

        await provider.SearchAsync(Center, 200, TestContext.Current.CancellationToken);
        var requestsAfterFirst = bucket.Requests.Count;
        await provider.SearchAsync(Center, 200, TestContext.Current.CancellationToken);

        bucket.Requests.Should().HaveCount(requestsAfterFirst);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task SearchAsync_ShouldThrowTheTypedUnavailableException_WhenTheBucketFails(HttpStatusCode status)
    {
        var (provider, _) = Create(bucket =>
        {
            bucket.AddRelease("r1", SiteTileWith([]));
            bucket.FailWith = status;
        });

        var act = async () => await provider.SearchAsync(Center, 200);

        await act.Should().ThrowAsync<BuildingProviderUnavailableException>();
    }

    [Fact]
    public async Task SearchAsync_ShouldReturnEmptyWithoutAnyRequest_WhenDisabled()
    {
        var (provider, bucket) = Create(
            bucket => bucket.AddRelease("r1", SiteTileWith([Building("site", Square(2040, 2040, 16))])),
            new OvertureBuildingsOptions { BucketUrl = OvertureTestBucket.BucketUrl, Enabled = false });

        (await provider.SearchAsync(Center, 200, TestContext.Current.CancellationToken)).Buildings.Should().BeEmpty();
        bucket.Requests.Should().BeEmpty();
    }
}
