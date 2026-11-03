using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.SiteAnalysis;
using AskLucy.Application.SiteAnalysis.Providers;
using AskLucy.Application.SiteAnalysis.Tools;
using AskLucy.Domain.SiteAnalysis;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using SiteAnalysisAggregate = AskLucy.Domain.SiteAnalysis.SiteAnalysis;

namespace AskLucy.Application.Tests.SiteAnalysis;

/// <summary>
/// specs/089-site-geometry-specialist — the geometry specialist's delivery, provenance, honest
/// unavailable figures, and failure behaviour. Mirrors <see cref="EnvironmentalContextToolTests"/>'s
/// substitution style and <see cref="SiteAnalysisResultRelayTests"/>'s relay assertions.
/// </summary>
public sealed class SiteGeometryToolTests
{
    private const string UserId = "user-1";
    private const string SiteName = "Al Barsha South";
    private const double Latitude = 25.09;
    private const double Longitude = 55.20;
    private const double MetresPerDegreeLatitude = 111_320.0;

    private static readonly GeoPoint Centre = new(Latitude, Longitude);

    private static readonly string[] AllowedKinds = ["heading", "text", "keyValue", "divider"];

    private readonly Guid _analysisId = Guid.CreateVersion7();

    private readonly ISiteAnalysisRepository _repository = Substitute.For<ISiteAnalysisRepository>();
    private readonly ISiteGeometryDataProvider _data = Substitute.For<ISiteGeometryDataProvider>();
    private readonly ISiteAnalysisResultRelay _relay = Substitute.For<ISiteAnalysisResultRelay>();

    private SiteAnalysisResultMetadata? _metadata;
    private JsonDocument? _content;
    private Guid? _documentId;
    private SiteAnalysisType? _analysisType;

    public SiteGeometryToolTests()
    {
        _relay
            .ReportSuccessAsync(
                Arg.Any<Guid>(), Arg.Any<SiteAnalysisType>(), Arg.Any<SiteAnalysisResultMetadata>(),
                Arg.Any<JsonDocument>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _analysisType = call.Arg<SiteAnalysisType>();
                _metadata = call.Arg<SiteAnalysisResultMetadata>();
                _documentId = call.Arg<Guid?>();
                _content = JsonDocument.Parse(call.Arg<JsonDocument>()!.RootElement.GetRawText());
                return Task.CompletedTask;
            });
    }

    private SiteGeometryTool BuildTool(string? boundaryGeoJson, SiteAnalysisAggregate? analysis = null)
    {
        analysis ??= SiteAnalysisAggregate.Create(
            UserId, Guid.NewGuid(), SiteName, Latitude, Longitude, boundaryGeoJson, expectedResultCount: 3, UserId);

        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(analysis);
        _data.ResolveAsync(Arg.Any<GeoPoint>(), Arg.Any<IReadOnlyList<GeoPoint>?>(), Arg.Any<CancellationToken>())
            .Returns(DataResolutionOutcome.Confirmed(SiteGeometryFeatureData.Empty));

        var services = new ServiceCollection();
        services.AddSingleton(_repository);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new SiteGeometryTool(
            scopeFactory, new SiteAnalysisContentComposer(), new SiteGeometryCalculator(), _data, _relay,
            NullLogger<SiteGeometryTool>.Instance);
    }

    private JsonDocument Input() =>
        JsonDocument.Parse(
            $$"""{"siteAnalysisId":"{{_analysisId}}","siteName":"Ignored name","siteLocation":"Ignored location"}""");

    private static AgentToolExecutionContext Context() =>
        new(Guid.NewGuid(), Guid.NewGuid(), UserId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    private static GeoPoint Offset(double east, double north) =>
        new(
            Centre.Latitude + (north / MetresPerDegreeLatitude),
            Centre.Longitude + (east / (MetresPerDegreeLatitude * Math.Cos(Centre.Latitude * Math.PI / 180.0))));

    private static List<GeoPoint> Rectangle(double lengthMeters, double widthMeters)
    {
        var halfLength = lengthMeters / 2.0;
        var halfWidth = widthMeters / 2.0;
        var ring = new List<GeoPoint>
        {
            Offset(-halfLength, -halfWidth),
            Offset(halfLength, -halfWidth),
            Offset(halfLength, halfWidth),
            Offset(-halfLength, halfWidth),
        };
        ring.Add(ring[0]);
        return ring;
    }

    private static string Snapshot() => JsonSerializer.Serialize(Rectangle(100, 60));

    private static SiteGeometryParcel Parcel() =>
        new("osm_way_1", Rectangle(200, 200), new Dictionary<string, string> { ["boundary"] = "parcel" });

    private static SiteGeometryStreet Street() =>
        new("osm_way_2", "Test Street", [Offset(-60, -50), Offset(60, -50)]);

    private static IReadOnlyList<string> BlockKinds(JsonDocument content) =>
        [.. content.RootElement.GetProperty("blocks").EnumerateArray().Select(block => block.GetProperty("kind").GetString()!)];

    /// <summary>Every string the content document carries, decoded — the raw JSON escapes non-ASCII (—, ²).</summary>
    private static string AllText(JsonDocument content) =>
        string.Join("\n", Strings(content.RootElement));

    private static IEnumerable<string> Strings(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    foreach (var value in Strings(property.Value))
                    {
                        yield return value;
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var value in Strings(item))
                    {
                        yield return value;
                    }
                }

                break;

            case JsonValueKind.String:
                yield return element.GetString()!;
                break;
        }
    }

    // ---------- delivery and provenance ----------

    [Fact]
    public async Task ExecuteAsync_ShouldReportTheGeometryFinding_ThroughTheRelay()
    {
        var tool = BuildTool(Snapshot());

        var result = await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.GetProperty("reported").GetBoolean().Should().BeTrue();
        await _relay.Received(1).ReportSuccessAsync(
            _analysisId, SiteAnalysisType.SiteGeometry, Arg.Any<SiteAnalysisResultMetadata>(),
            Arg.Any<JsonDocument>(), null, Arg.Any<CancellationToken>());
        _analysisType.Should().Be(SiteAnalysisType.SiteGeometry);
        _documentId.Should().BeNull("the geometry finding carries no document");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReportHighConfidence_WhenAParcelOutlineIsRecorded()
    {
        var tool = BuildTool(Snapshot());
        _data.ResolveAsync(Arg.Any<GeoPoint>(), Arg.Any<IReadOnlyList<GeoPoint>?>(), Arg.Any<CancellationToken>())
            .Returns(DataResolutionOutcome.Confirmed(new SiteGeometryFeatureData([Parcel()], [Street()])));

        await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        _metadata!.ConfidenceLevel.Should().Be(SiteAnalysisConfidenceLevel.High);
        _metadata.DataSource.Should().Be("OpenStreetMap (Overpass)");
        _content!.RootElement.GetRawText().Should().Contain("Recorded in OpenStreetMap");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReportMediumAndSayNoParcelIsRecorded_WhenNoneIs()
    {
        var tool = BuildTool(Snapshot());

        await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        _metadata!.ConfidenceLevel.Should().Be(SiteAnalysisConfidenceLevel.Medium);
        _metadata.Notes.Should().Contain(note => note.Contains("Parcel boundary", StringComparison.Ordinal));
        AllText(_content!).Should().Contain("No parcel or plot boundary is recorded");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReportEveryShapeFigureUnavailable_WhenNoBoundaryWasResolved()
    {
        var tool = BuildTool(boundaryGeoJson: null);

        var result = await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeTrue();
        _metadata!.ConfidenceLevel.Should().Be(SiteAnalysisConfidenceLevel.Low);
        _metadata.DataSource.Should().Be("OpenStreetMap (Overpass)", "the query answered; the site simply has no resolved shape");
        AllText(_content!).Should().Contain(SiteBoundarySnapshot.MissingReason);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldComposeOnlyTheExistingBlockKinds()
    {
        var tool = BuildTool(Snapshot());
        _data.ResolveAsync(Arg.Any<GeoPoint>(), Arg.Any<IReadOnlyList<GeoPoint>?>(), Arg.Any<CancellationToken>())
            .Returns(DataResolutionOutcome.Confirmed(new SiteGeometryFeatureData([Parcel()], [Street()])));

        await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        BlockKinds(_content!).Should().OnlyContain(kind => AllowedKinds.Contains(kind));
        BlockKinds(_content!).Should().NotContain("image", "imagery belongs to the schematic specialist");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldStateTheMeasurementBasis_InTheFinding()
    {
        var tool = BuildTool(Snapshot());

        await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        AllText(_content!).Should().Contain("The site boundary resolved for this analysis");
    }

    // ---------- source unavailable vs genuine fault ----------

    [Fact]
    public async Task ExecuteAsync_ShouldStillDeliverBoundaryFigures_WhenTheSourceIsUnavailable()
    {
        var tool = BuildTool(Snapshot());
        _data.ResolveAsync(Arg.Any<GeoPoint>(), Arg.Any<IReadOnlyList<GeoPoint>?>(), Arg.Any<CancellationToken>())
            .Returns(DataResolutionOutcome.Unavailable<SiteGeometryFeatureData>("Overpass is down."));

        var result = await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeTrue("a source outage is a figure-level outcome, not a specialist failure");
        await _relay.Received(1).ReportSuccessAsync(
            _analysisId, SiteAnalysisType.SiteGeometry, Arg.Any<SiteAnalysisResultMetadata>(),
            Arg.Any<JsonDocument>(), null, Arg.Any<CancellationToken>());
        await _relay.DidNotReceive().ReportFailureAsync(
            Arg.Any<Guid>(), Arg.Any<SiteAnalysisType>(), Arg.Any<string>(), Arg.Any<Exception?>(), Arg.Any<CancellationToken>());

        _metadata!.Notes.Should().Contain(note => note.Contains("Overpass is down.", StringComparison.Ordinal));
        AllText(_content!).Should().Contain("Area", "the boundary-derived measurements are still delivered");
        AllText(_content!).Should().Contain("Unavailable — Overpass is down.");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReportFailure_WhenTheAnalysisDoesNotExist()
    {
        var tool = BuildTool(Snapshot());
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((SiteAnalysisAggregate?)null);

        var result = await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeFalse();
        await _relay.Received(1).ReportFailureAsync(
            _analysisId, SiteAnalysisType.SiteGeometry,
            "The site analysis this finding belongs to could not be found.", null, Arg.Any<CancellationToken>());
        await _relay.DidNotReceive().ReportSuccessAsync(
            Arg.Any<Guid>(), Arg.Any<SiteAnalysisType>(), Arg.Any<SiteAnalysisResultMetadata>(),
            Arg.Any<JsonDocument>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReportFailure_WhenTheProviderThrows()
    {
        var tool = BuildTool(Snapshot());
        _data.ResolveAsync(Arg.Any<GeoPoint>(), Arg.Any<IReadOnlyList<GeoPoint>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<DataResolutionOutcome<SiteGeometryFeatureData>>(
                new InvalidOperationException("boom")));

        var result = await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeFalse();
        await _relay.Received(1).ReportFailureAsync(
            _analysisId, SiteAnalysisType.SiteGeometry, "boom", Arg.Any<Exception>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnFailure_WithoutReporting_WhenTheIdIsMissing()
    {
        var tool = BuildTool(Snapshot());

        var result = await tool.ExecuteAsync(Context(), JsonDocument.Parse("{}"), TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeFalse();
        await _relay.DidNotReceiveWithAnyArgs().ReportSuccessAsync(
            default, default, default!, default!, default, TestContext.Current.CancellationToken);
        await _relay.DidNotReceiveWithAnyArgs().ReportFailureAsync(
            default, default, default!, default, TestContext.Current.CancellationToken);
    }
}
