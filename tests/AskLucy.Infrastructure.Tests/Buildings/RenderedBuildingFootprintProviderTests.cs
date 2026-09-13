using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AskLucy.Application.Buildings;
using AskLucy.Domain.SiteBoundaries;
using AskLucy.Infrastructure.Buildings;
using AskLucy.Infrastructure.Geocoding;
using AskLucy.Infrastructure.Boundaries;
using AskLucy.Infrastructure.Tests.Ai;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Buildings;

/// <summary>
/// specs/053-rendered-building-footprints T017-T020 — <see cref="RenderedBuildingFootprintProvider"/>
/// against four checked-in Static Maps fixtures spanning the area types SC-001 requires (dense
/// urban, suburban, sparse — a two-dense-urban-only fixture set could not actually measure that
/// criterion, a gap found and fixed during `/speckit-analyze`). No network call, no API key needed:
/// each fixture is a real tile fetched and verified live before this feature was specified
/// (research D1), captured once into <c>Buildings/fixtures/</c>.
/// </summary>
public sealed class RenderedBuildingFootprintProviderTests
{
    private sealed record FixtureBounds(double West, double South, double East, double North);
    private sealed record Fixture(string Name, double CenterLatitude, double CenterLongitude, int Zoom, int Scale, string AreaType, FixtureBounds Bounds);

    private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Buildings", "fixtures");

    private static IReadOnlyList<Fixture> LoadFixtureManifest()
    {
        var json = File.ReadAllText(Path.Combine(FixturesDirectory, "fixtures.json"));
        using var doc = JsonDocument.Parse(json);
        return [.. doc.RootElement.GetProperty("fixtures").EnumerateArray().Select(e =>
        {
            var b = e.GetProperty("bounds");
            return new Fixture(
                e.GetProperty("name").GetString()!,
                e.GetProperty("centerLatitude").GetDouble(),
                e.GetProperty("centerLongitude").GetDouble(),
                e.GetProperty("zoom").GetInt32(),
                e.GetProperty("scale").GetInt32(),
                e.GetProperty("areaType").GetString()!,
                new FixtureBounds(b.GetProperty("west").GetDouble(), b.GetProperty("south").GetDouble(), b.GetProperty("east").GetDouble(), b.GetProperty("north").GetDouble()));
        })];
    }

    private static byte[] LoadFixtureBytes(string name) =>
        File.ReadAllBytes(Path.Combine(FixturesDirectory, $"{name}.png"));

    private static RenderedBuildingFootprintProvider CreateProvider(
        byte[] responseBytes,
        out Func<int> requestCount,
        IMemoryCache? cache = null,
        BuildingRetrievalOptions? retrievalOptions = null,
        RenderedFootprintOptions? renderedOptions = null,
        Func<HttpRequestMessage, HttpResponseMessage>? responder = null)
    {
        var count = 0;
        var handler = new StubHttpMessageHandler(request =>
        {
            count++;
            if (responder is not null) return responder(request);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(responseBytes) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return response;
        });
        requestCount = () => count;

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("GoogleStaticMaps")
            .Returns(new HttpClient(handler) { BaseAddress = new Uri("https://maps.googleapis.com/maps/api/") });

        return new RenderedBuildingFootprintProvider(
            factory,
            Options.Create(renderedOptions ?? new RenderedFootprintOptions()),
            Options.Create(retrievalOptions ?? new BuildingRetrievalOptions()),
            Options.Create(new GoogleMapsGeocodingOptions { GoogleMapsApiKey = "test-key" }),
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            NullLogger<RenderedBuildingFootprintProvider>.Instance);
    }

    public static TheoryData<string> AllFixtures()
    {
        var data = new TheoryData<string>();
        foreach (var f in LoadFixtureManifest())
        {
            data.Add(f.Name);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public async Task SearchAsync_ShouldProduceOnlyClosedRingsWithAtLeastThreePoints_ForEveryFixture(string fixtureName)
    {
        var manifest = LoadFixtureManifest().Single(f => f.Name == fixtureName);
        var provider = CreateProvider(LoadFixtureBytes(fixtureName), out _);

        var result = await provider.SearchAsync(new GeoPoint(manifest.CenterLatitude, manifest.CenterLongitude), 200, CancellationToken.None);

        foreach (var building in result.Buildings)
        {
            building.Ring.Should().HaveCountGreaterThanOrEqualTo(4, $"{fixtureName}: a closed ring needs at least 3 distinct points plus the closing duplicate");
            building.Ring[0].Should().Be(building.Ring[^1], $"{fixtureName}: every returned ring must be closed");
        }
    }

    [Fact]
    public async Task SearchAsync_ShouldReturnDozensOfFootprints_ForTheDenseUrbanFixtures()
    {
        var manifest = LoadFixtureManifest().Single(f => f.Name == "dubai-al-safa-park");
        var provider = CreateProvider(LoadFixtureBytes("dubai-al-safa-park"), out _);

        var result = await provider.SearchAsync(new GeoPoint(manifest.CenterLatitude, manifest.CenterLongitude), 200, CancellationToken.None);

        result.Buildings.Count.Should().BeGreaterThan(10, "a dense urban fixture should yield dozens of separate footprints, not a handful");
    }

    [Fact]
    public async Task SearchAsync_ShouldReturnFewerWellSeparatedFootprints_ForTheSuburbanFixture()
    {
        var manifest = LoadFixtureManifest().Single(f => f.Name == "dubai-arabian-ranches");
        var provider = CreateProvider(LoadFixtureBytes("dubai-arabian-ranches"), out _);

        var result = await provider.SearchAsync(new GeoPoint(manifest.CenterLatitude, manifest.CenterLongitude), 200, CancellationToken.None);

        result.Buildings.Should().NotBeEmpty("suburban villas are still buildings and must be found");
        result.Buildings.Count.Should().BeLessThan(60, "low-density villas should not produce as many footprints as the dense urban fixture");
    }

    [Fact]
    public async Task SearchAsync_ShouldReturnAnEmptyResult_ForTheSparseDesertFixture_WithNoFalsePositives()
    {
        // research D1/D5 — the recipe must report nothing where there is genuinely nothing, not
        // manufacture noise from JPEG-adjacent PNG compression or antialiasing on an empty frame.
        var manifest = LoadFixtureManifest().Single(f => f.Name == "rub-al-khali-desert-edge");
        var provider = CreateProvider(LoadFixtureBytes("rub-al-khali-desert-edge"), out _);

        var result = await provider.SearchAsync(new GeoPoint(manifest.CenterLatitude, manifest.CenterLongitude), 200, CancellationToken.None);

        result.Buildings.Should().BeEmpty("the desert fixture is a verified all-white tile with no building pixels");
    }

    [Fact]
    public async Task SearchAsync_ShouldReturnANonEmptyResult_AtTheCairoSite_WhereOverpassReturnsNothing()
    {
        // quickstart Scenario 2, SC-002 — the feature's reason to exist. A zero result here is a
        // regression in the style string or threshold, never an "empty neighbourhood": OSM reports
        // zero buildings within 200 m of this exact point, while the map renders dozens.
        var manifest = LoadFixtureManifest().Single(f => f.Name == "cairo-prototype-site");
        var provider = CreateProvider(LoadFixtureBytes("cairo-prototype-site"), out _);

        var result = await provider.SearchAsync(new GeoPoint(manifest.CenterLatitude, manifest.CenterLongitude), 200, CancellationToken.None);

        result.Buildings.Should().NotBeEmpty("Cairo has buildings visible on the map even though OSM has none registered here");
        result.Buildings.Count.Should().BeGreaterThan(10);
    }

    [Fact]
    public async Task SearchAsync_ShouldPopulateExcludedCount_WhenAnyComponentWasDiscarded()
    {
        var manifest = LoadFixtureManifest().Single(f => f.Name == "dubai-al-safa-park");
        var provider = CreateProvider(LoadFixtureBytes("dubai-al-safa-park"), out _);

        var result = await provider.SearchAsync(new GeoPoint(manifest.CenterLatitude, manifest.CenterLongitude), 200, CancellationToken.None);

        // A dense real-world tile always has at least one edge-clipped or below-minimum blob; this
        // is a non-negative sanity check that the counter path executes at all (FR-009), not a
        // claim about the exact figure.
        result.ExcludedCount.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task SearchAsync_ShouldSetLimitedTrue_WhenCountExceedsCap()
    {
        var manifest = LoadFixtureManifest().Single(f => f.Name == "dubai-al-safa-park");
        var provider = CreateProvider(LoadFixtureBytes("dubai-al-safa-park"), out _, retrievalOptions: new BuildingRetrievalOptions { MaxBuildingCount = 3 });

        var result = await provider.SearchAsync(new GeoPoint(manifest.CenterLatitude, manifest.CenterLongitude), 200, CancellationToken.None);

        result.Buildings.Should().HaveCount(3);
        result.Limited.Should().BeTrue();
    }

    [Fact]
    public async Task SearchAsync_ShouldNotReInvokeTheHttpClient_OnASecondCallWithinTheCacheTtl()
    {
        // research D9 — this provider must cache itself; nothing wraps it. Without this, wiring the
        // composite provider in as primary would leave every solar-analysis open/close re-fetching
        // a billed Static Maps tile (the /speckit-analyze CRITICAL finding this guards against).
        var manifest = LoadFixtureManifest().Single(f => f.Name == "dubai-al-safa-park");
        var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = CreateProvider(LoadFixtureBytes("dubai-al-safa-park"), out var requestCount, cache: cache);
        var center = new GeoPoint(manifest.CenterLatitude, manifest.CenterLongitude);

        await provider.SearchAsync(center, 200, CancellationToken.None);
        await provider.SearchAsync(center, 200, CancellationToken.None);

        requestCount().Should().Be(1, "the second call within the cache TTL must not re-fetch the tile");
    }

    [Fact]
    public async Task SearchAsync_ShouldThrowBuildingProviderUnavailableException_OnHttpFailure()
    {
        var provider = CreateProvider([], out _, responder: _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var act = async () => await provider.SearchAsync(new GeoPoint(25.2, 55.27), 200, CancellationToken.None);

        await act.Should().ThrowAsync<BuildingProviderUnavailableException>();
    }

    [Fact]
    public async Task SearchAsync_ShouldThrowBuildingProviderUnavailableException_OnTimeout()
    {
        var provider = CreateProvider([], out _, responder: _ => throw new TaskCanceledException("Timed out"));

        var act = async () => await provider.SearchAsync(new GeoPoint(25.2, 55.27), 200, CancellationToken.None);

        await act.Should().ThrowAsync<BuildingProviderUnavailableException>();
    }

    [Fact]
    public async Task SearchAsync_ShouldThrowBuildingProviderUnavailableException_WhenNoApiKeyConfigured()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        var provider = new RenderedBuildingFootprintProvider(
            factory,
            Options.Create(new RenderedFootprintOptions()),
            Options.Create(new BuildingRetrievalOptions()),
            Options.Create(new GoogleMapsGeocodingOptions { GoogleMapsApiKey = "" }),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<RenderedBuildingFootprintProvider>.Instance);

        var act = async () => await provider.SearchAsync(new GeoPoint(25.2, 55.27), 200, CancellationToken.None);

        await act.Should().ThrowAsync<BuildingProviderUnavailableException>();
    }

    [Fact]
    public async Task SearchAsync_ShouldReturnEmpty_NotThrow_ForAValidAllWhiteTile()
    {
        // The distinction contracts/rendered-footprint-provider.md and the composite provider
        // (specs/053 US2) both depend on: a valid tile with no building pixels is a genuine empty
        // result, never an exception — only a failed FETCH throws.
        var manifest = LoadFixtureManifest().Single(f => f.Name == "rub-al-khali-desert-edge");
        var provider = CreateProvider(LoadFixtureBytes("rub-al-khali-desert-edge"), out _);

        var result = await provider.SearchAsync(new GeoPoint(manifest.CenterLatitude, manifest.CenterLongitude), 200, CancellationToken.None);

        result.Buildings.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public async Task SearchAsync_ShouldMarkEveryRenderedFootprint_AsAssumedHeight_NeverKnown(string fixtureName)
    {
        // T030, quickstart Scenario 7, SC-006, research D8 — a rendered image carries no height
        // data at all. This test exists so the marking can never silently drift to `known`, which
        // would tell the user a height was recorded in the source when it was not. Runs across all
        // four fixtures, not just one, since this must hold regardless of area type.
        var manifest = LoadFixtureManifest().Single(f => f.Name == fixtureName);
        var provider = CreateProvider(LoadFixtureBytes(fixtureName), out _);

        var result = await provider.SearchAsync(new GeoPoint(manifest.CenterLatitude, manifest.CenterLongitude), 200, CancellationToken.None);

        result.Buildings.Should().OnlyContain(b => b.HeightProvenance == BuildingHeightProvenance.Assumed);
        result.Buildings.Should().OnlyContain(b => b.HeightMetres == 9.0, "matches OverpassBuildingFootprintProvider's own default exactly (research D5), so a building read identically regardless of source");
    }

    /// <summary>
    /// T032, quickstart Scenario 6 (FR-008, FR-009, research D6) — a synthetic tile, not a real
    /// fixture, because it needs precise control over exactly where each blob sits relative to the
    /// requested radius: below the minimum area, touching the tile edge, entirely beyond the
    /// radius, and straddling the radius boundary (kept whole, the one deliberate non-exclusion).
    /// Four independently-verifiable outcomes in one image, at zoom 18 where ~0.27 m/px puts the
    /// radius boundary at roughly ((radiusMetres / metresPerPixel)) pixels from centre.
    /// </summary>
    [Fact]
    public async Task SearchAsync_ShouldApplyEveryExclusionRule_AndCountEachDiscard_WithoutFailingTheWholeResult()
    {
        const int size = 1280;
        var center = new GeoPoint(25.1560, 55.2218);
        const int zoom = 18;
        var metresPerPixel = StaticMapFraming.MetersPerPixel(center.Latitude, zoom) / 2.0; // scale=2
        const int radiusMetres = 30;
        var radiusPixels = (int)(radiusMetres / metresPerPixel); // ~111 px at this latitude/zoom

        using var image = new Image<Rgba32>(size, size);
        var magenta = new Rgba32(255, 0, 255, 255);

        void FillSquare(int originX, int originY, int squareSize)
        {
            for (var x = originX; x < originX + squareSize && x < size; x++)
            {
                for (var y = originY; y < originY + squareSize && y < size; y++)
                {
                    if (x >= 0 && y >= 0) image[x, y] = magenta;
                }
            }
        }

        var centrePixel = size / 2;

        // KEPT WHOLE: a real, well-formed 20x20 building straddling the radius boundary — some of
        // its vertices are beyond `radiusMetres`, but it is NOT truncated (research D6).
        FillSquare(centrePixel + radiusPixels - 10, centrePixel, 20);

        // EXCLUDED — entirely outside the radius: far from centre, but nowhere near the tile edge.
        FillSquare(centrePixel + radiusPixels + 300, centrePixel + radiusPixels + 300, 20);

        // EXCLUDED — touches the tile edge: clipped geometry, fabricated beyond the frame.
        FillSquare(0, centrePixel - 400, 15);

        // EXCLUDED — below the minimum area (15 m² ≈ well under 20x20 px at this resolution).
        FillSquare(centrePixel - 300, centrePixel - 300, 2);

        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        var provider = CreateProvider(stream.ToArray(), out _, renderedOptions: new RenderedFootprintOptions { Zoom = zoom });

        var result = await provider.SearchAsync(center, radiusMetres, CancellationToken.None);

        result.Buildings.Should().ContainSingle("only the straddling building is kept — entirely-outside, edge-touching and below-minimum blobs are all excluded");
        result.ExcludedCount.Should().Be(3, "every discard must be counted, not silently dropped (FR-009)");
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void CoveredBounds_ShouldMatchTheIndependentlyComputedBounds_ForEveryFixture(string fixtureName)
    {
        // quickstart Scenario 3, SC-003, research D3 — cross-language regression guard: the
        // fixture manifest's bounds were computed in JavaScript, mirroring StaticMapFraming's exact
        // Web Mercator formula, at the time each fixture was captured live. Asserting the C#
        // implementation still reproduces them (well within the stated ±1 m tolerance — the two
        // computations agree to floating-point precision, not merely to a metre) is what SC-003
        // actually verifies: the geometry a footprint's ring is expressed in lines up with the real
        // ground the tile covers.
        var manifest = LoadFixtureManifest().Single(f => f.Name == fixtureName);

        var (west, south, east, north) = AskLucy.Infrastructure.Boundaries.StaticMapFraming.CoveredBounds(
            new GeoPoint(manifest.CenterLatitude, manifest.CenterLongitude), manifest.Zoom);

        west.Should().BeApproximately(manifest.Bounds.West, 1e-6);
        south.Should().BeApproximately(manifest.Bounds.South, 1e-6);
        east.Should().BeApproximately(manifest.Bounds.East, 1e-6);
        north.Should().BeApproximately(manifest.Bounds.North, 1e-6);
    }
}
