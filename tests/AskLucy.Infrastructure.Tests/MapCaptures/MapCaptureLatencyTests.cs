using System.Diagnostics;
using AskLucy.Application.Buildings;
using AskLucy.Application.Locations;
using AskLucy.Application.MapCaptures;
using AskLucy.Domain.SiteBoundaries;
using AskLucy.Infrastructure.Geocoding;
using AskLucy.Infrastructure.MapCaptures;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.MapCaptures;

/// <summary>
/// Opens the wall-clock capture measurement. It bills a real Google key and depends on a real
/// provider's latency, so it runs only on request — the same rule as
/// <c>ScalePerformanceGate</c> and <c>AccountEmailLatencyGate</c>.
/// </summary>
public static class MapCaptureLatencyGate
{
    public const string EnvironmentVariable = "RUN_SCALE_PERFORMANCE_TESTS";

    public const string SkipReason =
        "A wall-clock measurement against a real imagery provider: set RUN_SCALE_PERFORMANCE_TESTS=1 to run it.";

    public static bool NotRequested => Environment.GetEnvironmentVariable(EnvironmentVariable) != "1";
}

/// <summary>
/// The Google Maps key the measurement runs against. It is read from the environment, never written
/// down here: the test is skipped unless the scale gate is open, and then it uses the developer's
/// own key.
/// </summary>
internal static class MapCaptureScaleKey
{
    public const string EnvironmentVariable = "GOOGLE_MAPS_API_KEY";

    public static string Value => Environment.GetEnvironmentVariable(EnvironmentVariable) ?? string.Empty;

    public static string Missing =>
        $"{EnvironmentVariable} is not set; the measurement needs a real Google Maps key.";
}

/// <summary>
/// specs/083-viewer-map-capture SC-001 — 95% of valid capture requests complete within 10 seconds,
/// geocoding included. Gated: <c>RUN_SCALE_PERFORMANCE_TESTS=1</c>.
/// <para>
/// Measured here rather than at the Web level: the capture path itself touches no database, but a
/// Web-level host must start, and its startup reaches the shared test SQL Server. That makes the
/// measurement depend on a host the measurement is not about — the shared database has already
/// produced a connection failure here that had nothing to do with the capture. This suite talks to
/// the real geocoder and the real renderer and nothing else.
/// </para>
/// </summary>
public sealed class MapCaptureLatencyTests(ITestOutputHelper output)
{
    private const int Captures = 20;

    [Fact(Skip = MapCaptureLatencyGate.SkipReason, SkipWhen = nameof(MapCaptureLatencyGate.NotRequested), SkipType = typeof(MapCaptureLatencyGate))]
    public async Task SequentialCaptures_P95IsUnderTenSeconds()
    {
        MapCaptureScaleKey.Value.Should().NotBeNullOrWhiteSpace(MapCaptureScaleKey.Missing);

        var ct = TestContext.Current.CancellationToken;
        var (geocoder, renderer) = Create();

        var durations = new List<TimeSpan>(Captures);
        for (var i = 0; i < Captures; i++)
        {
            var clock = Stopwatch.StartNew();

            var candidates = await geocoder.SearchAsync("Burjuman", ct);
            var match = candidates.Count > 0 ? candidates[0] : null;
            match.Should().NotBeNull("the geocoder must resolve the query");

            var result = await renderer.RenderAsync(
                new MapImageRequest(
                    match!.Latitude,
                    match.Longitude,
                    "Burjuman",
                    MapCaptureMode.Satellite,
                    [],
                    1000,
                    "scale-pipeline"),
                ct);

            clock.Stop();

            result.IsSuccess.Should().BeTrue("capture {0} failed: {1}", i, result.FailureDetail);
            durations.Add(clock.Elapsed);
        }

        var p95 = Percentile(durations, 0.95);
        output.WriteLine(
            $"SC-001 {durations.Count} sequential captures: p95={p95.TotalSeconds:F3}s "
            + $"min={durations.Min().TotalSeconds:F3}s max={durations.Max().TotalSeconds:F3}s");

        p95.Should().BeLessThan(
            TimeSpan.FromSeconds(10),
            "SC-001: 95% of valid capture requests complete within 10 seconds, geocoding included");
    }

    /// <summary>The real geocoder and the real renderer, wired to the real HTTP clients.</summary>
    private static (IGeocodingProvider Geocoder, IMapImageRenderer Renderer) Create()
    {
        var factory = new RealHttpClientFactory();

        var footprints = Substitute.For<IBuildingFootprintProvider>();
        footprints.SearchAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new BuildingFootprintResult([], false, 0, 500));

        var googleOptions = Options.Create(new GoogleMapsGeocodingOptions
        {
            GoogleMapsApiKey = MapCaptureScaleKey.Value,
        });

        var composer = new MapImageComposer(
            Options.Create(new MapCaptureOptions()), NullLogger<MapImageComposer>.Instance);

        var geocoder = new GoogleMapsGeocodingProvider(
            factory, googleOptions, NullLogger<GoogleMapsGeocodingProvider>.Instance);

        var renderer = new GoogleStaticMapImageRenderer(
            factory, googleOptions, footprints, composer, NullLogger<GoogleStaticMapImageRenderer>.Instance);

        return (geocoder, renderer);
    }

    private static TimeSpan Percentile(IReadOnlyList<TimeSpan> samples, double percentile)
    {
        var ordered = samples.OrderBy(d => d).ToArray();
        var index = (int)Math.Ceiling(percentile * ordered.Length) - 1;
        return ordered[Math.Clamp(index, 0, ordered.Length - 1)];
    }

    /// <summary>
    /// The two named clients the app registers, with the same base addresses and timeouts
    /// (<c>AddInfrastructure</c>), so the measurement sees the app's real network settings.
    /// </summary>
    private sealed class RealHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => name switch
        {
            "GoogleStaticMaps" => new HttpClient
            {
                BaseAddress = new Uri("https://maps.googleapis.com/maps/api/"),
                Timeout = TimeSpan.FromSeconds(30),
            },
            "Geocoding" => new HttpClient
            {
                BaseAddress = new Uri("https://maps.googleapis.com/maps/api/"),
                Timeout = TimeSpan.FromSeconds(30),
            },
            _ => new HttpClient { Timeout = TimeSpan.FromSeconds(30) },
        };
    }
}
