using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.SiteBoundaries;

/// <summary>
/// specs/042-site-boundary-resolution T021/T038 — <see cref="BoundaryResolutionService"/> routes
/// every candidate-search outcome to the correct <see cref="BoundaryResolutionOutcomeType"/> and
/// never throws (constitution §2.VIII).
/// </summary>
public sealed class BoundaryResolutionServiceTests
{
    private readonly IBoundaryCandidateProvider _candidateProvider = Substitute.For<IBoundaryCandidateProvider>();
    private readonly ISatelliteImageProvider _satelliteImageProvider = Substitute.For<ISatelliteImageProvider>();
    private readonly IBoundaryVisionAnalyzer _visionAnalyzer = Substitute.For<IBoundaryVisionAnalyzer>();
    private readonly BoundaryScoringOptions _options = new();
    private readonly BoundaryResolutionService _service;

    private static readonly Guid ChatId = Guid.NewGuid();
    private static readonly ConfirmedLocationData AlSafaLocation = new(25.1560, 55.2218, "Al Safa Park 2", 0.9);

    private static readonly SiteBoundaryPolygon SamplePolygon = new([
        new GeoPoint(25.1560, 55.2210), new GeoPoint(25.1560, 55.2220),
        new GeoPoint(25.1550, 55.2220), new GeoPoint(25.1550, 55.2210), new GeoPoint(25.1560, 55.2210),
    ]);

    private static readonly SatelliteImage SampleImage = new([1, 2, 3], "image/png", 55.2200, 25.1540, 55.2240, 25.1580);

    public BoundaryResolutionServiceTests()
    {
        // AI vision verification is off by default in these tests unless a test explicitly wires
        // up _satelliteImageProvider/_visionAnalyzer — an unconfigured NSubstitute returns null
        // for FetchAsync, which degrades to "ai_not_used" exactly like the feature being disabled,
        // so every pre-existing deterministic-only test keeps passing unchanged.
        var scorer = new BoundaryCandidateScorer(Microsoft.Extensions.Options.Options.Create(_options));
        _service = new BoundaryResolutionService(
            _candidateProvider, scorer, _satelliteImageProvider, _visionAnalyzer,
            Microsoft.Extensions.Options.Options.Create(_options), Substitute.For<ILogger<BoundaryResolutionService>>());
    }

    private static BoundaryCandidate Candidate(
        SiteBoundarySource source = SiteBoundarySource.OsmBoundary, string name = "Al Safa Park 2",
        IReadOnlyDictionary<string, string>? tags = null, double distanceMeters = 5, double areaSquareMeters = 15_000) =>
        new("osm_1", SamplePolygon, source, name, tags ?? new Dictionary<string, string> { ["leisure"] = "park" }, distanceMeters, areaSquareMeters);

    [Fact]
    public async Task ResolveAsync_ShouldReturnConfirmedWithHighConfidence_WhenStrongCandidateFound()
    {
        _candidateProvider.SearchAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<BoundaryCandidate> { Candidate() });

        var outcome = await _service.ResolveAsync(AlSafaLocation, ChatId);

        outcome.Type.Should().Be(BoundaryResolutionOutcomeType.Confirmed);
        outcome.ConfirmedBoundary.Should().NotBeNull();
        outcome.ConfirmedBoundary!.ConfidenceLevel.Should().Be(BoundaryConfidenceLevel.High);
        outcome.ConfirmedBoundary.Source.Should().Be(SiteBoundarySource.OsmBoundary);
        outcome.ConfirmedBoundary.SourceDetail.Should().Contain("OpenStreetMap");
        outcome.ConfirmationText.Should().Contain("Al Safa Park 2");
    }

    [Fact]
    public async Task ResolveAsync_ShouldReturnNoCandidatesWithManualFallback_WhenNoCandidatesFound()
    {
        _candidateProvider.SearchAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<BoundaryCandidate>());

        var outcome = await _service.ResolveAsync(AlSafaLocation, ChatId);

        outcome.Type.Should().Be(BoundaryResolutionOutcomeType.NoCandidates);
        outcome.ConfirmedBoundary.Should().NotBeNull("User Story 1 acceptance scenario 3 requires an actual approximate area to render");
        outcome.ConfirmedBoundary!.Source.Should().Be(SiteBoundarySource.ManualFallback);
        outcome.ConfirmedBoundary.ConfidenceLevel.Should().Be(BoundaryConfidenceLevel.Low);
        outcome.ConfirmedBoundary.Polygon.Should().HaveCountGreaterThanOrEqualTo(3);
        outcome.ConfirmationText.Should().Contain("approximate");
    }

    [Fact]
    public async Task ResolveAsync_ShouldReturnUnavailableWithNoBoundary_WhenCandidateProviderThrows()
    {
        _candidateProvider.SearchAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<BoundaryCandidate>>(new BoundaryProviderUnavailableException("Overpass down.")));

        var outcome = await _service.ResolveAsync(AlSafaLocation, ChatId);

        outcome.Type.Should().Be(BoundaryResolutionOutcomeType.Unavailable);
        outcome.ConfirmedBoundary.Should().BeNull("FR-012 forbids returning a default result when the source itself is unreachable");
        outcome.ConfirmationText.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ResolveAsync_ShouldPopulateAlternativeCandidateNames_WhenTwoCandidatesAreSimilarlyPlausible()
    {
        // Both candidates keep an exact name match and near-identical other factors so the
        // combined-score gap lands within AlternativeCandidateScoreMargin (0.05) — this is a
        // scoring-weighted margin, not a name-similarity margin, so the test constructs a
        // realistic near-tie explicitly rather than assuming any two "similar-looking" names
        // would produce one on their own.
        var winner = Candidate(name: "Al Safa Park 2", distanceMeters: 5);
        var closeSecond = Candidate(name: "Al Safa Park 2 (Landuse Boundary)", distanceMeters: 8, tags: new Dictionary<string, string> { ["landuse"] = "recreation_ground" });
        _candidateProvider.SearchAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<BoundaryCandidate> { winner, closeSecond });

        var outcome = await _service.ResolveAsync(AlSafaLocation, ChatId);

        outcome.Type.Should().Be(BoundaryResolutionOutcomeType.Confirmed);
        outcome.ConfirmedBoundary!.AlternativeCandidateNames.Should().Contain("Al Safa Park 2 (Landuse Boundary)");
        outcome.ConfirmationText.Should().Contain("Al Safa Park 2 (Landuse Boundary)");
    }

    [Fact]
    public async Task ResolveAsync_ShouldNotPopulateAlternativeCandidateNames_WhenOneCandidateClearlyDominates()
    {
        var winner = Candidate(source: SiteBoundarySource.GovernmentCadastral, name: "Al Safa Park 2", distanceMeters: 0);
        var farWeaker = Candidate(source: SiteBoundarySource.AiInterpretation, name: "Distant Unrelated Site", distanceMeters: 490, tags: new Dictionary<string, string>());
        _candidateProvider.SearchAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<BoundaryCandidate> { winner, farWeaker });

        var outcome = await _service.ResolveAsync(AlSafaLocation, ChatId);

        outcome.ConfirmedBoundary!.AlternativeCandidateNames.Should().BeEmpty();
    }

    // specs/042-site-boundary-resolution T044 (US4) — proves nothing in the resolution pipeline
    // is hardcoded to Al Safa Park 2: the identical High-confidence path applies to an
    // unrelated institutional site and a generic address, using their own coordinates/tags.
    [Theory]
    [InlineData("Dubai American Academy", "amenity", "school", 25.1122, 55.2001)]
    [InlineData("221B Baker Street", "building", "residential", 24.4667, 54.3667)]
    public async Task ResolveAsync_ShouldApplyTheSameConfirmedHighConfidenceBehavior_ForAnyUnrelatedSiteType(
        string siteName, string tagKey, string tagValue, double latitude, double longitude)
    {
        var location = new ConfirmedLocationData(latitude, longitude, siteName, 0.9);
        var candidate = Candidate(name: siteName, tags: new Dictionary<string, string> { [tagKey] = tagValue });
        _candidateProvider.SearchAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<BoundaryCandidate> { candidate });

        var outcome = await _service.ResolveAsync(location, ChatId);

        outcome.Type.Should().Be(BoundaryResolutionOutcomeType.Confirmed);
        outcome.ConfirmedBoundary!.SiteName.Should().Be(siteName);
        outcome.ConfirmedBoundary.ConfidenceLevel.Should().Be(BoundaryConfidenceLevel.High);
        outcome.ConfirmationText.Should().Contain(siteName);
    }

    [Theory]
    [InlineData("Dubai American Academy", 25.1122, 55.2001)]
    [InlineData("An Unmapped Street Address", 24.4667, 54.3667)]
    public async Task ResolveAsync_ShouldApplyTheSameNoCandidatesFallback_ForAnyUnrelatedSiteType(
        string siteName, double latitude, double longitude)
    {
        var location = new ConfirmedLocationData(latitude, longitude, siteName, 0.9);
        _candidateProvider.SearchAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<BoundaryCandidate>());

        var outcome = await _service.ResolveAsync(location, ChatId);

        outcome.Type.Should().Be(BoundaryResolutionOutcomeType.NoCandidates);
        outcome.ConfirmedBoundary!.Source.Should().Be(SiteBoundarySource.ManualFallback);
        outcome.ConfirmedBoundary.CentroidLatitude.Should().Be(latitude);
        outcome.ConfirmedBoundary.CentroidLongitude.Should().Be(longitude);
    }

    // specs/042-site-boundary-resolution — Gemini vision cross-check (select_final_candidate port).
    [Fact]
    public async Task ResolveAsync_ShouldKeepTopRankedCandidateWithBoostedConfidence_WhenAiVisionAgrees()
    {
        var winner = Candidate(name: "Al Safa Park 2", distanceMeters: 5);
        _candidateProvider.SearchAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<BoundaryCandidate> { winner });
        _satelliteImageProvider.FetchAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(SampleImage);
        _visionAnalyzer.AnalyzeAsync(SampleImage, Arg.Any<IReadOnlyList<ScoredBoundaryCandidate>>(), "Al Safa Park 2", Arg.Any<GeoPoint>(), Arg.Any<CancellationToken>())
            .Returns(new BoundaryVisionAnalysis(AiUsed: true, "osm_1", 0.9, "high", ["Matches visible park boundary."], [], false));

        var outcome = await _service.ResolveAsync(AlSafaLocation, ChatId);

        outcome.Type.Should().Be(BoundaryResolutionOutcomeType.Confirmed);
        outcome.ConfirmedBoundary!.Source.Should().Be(SiteBoundarySource.OsmBoundary);
        outcome.ConfirmedBoundary.ConfidenceLevel.Should().Be(BoundaryConfidenceLevel.High);
        outcome.ConfirmationText.Should().Contain("Gemini vision analysis");
    }

    [Fact]
    public async Task ResolveAsync_ShouldSwitchToAiSelectedCandidate_WhenAiVisionOverridesTheTopRankedCandidate()
    {
        var topRanked = Candidate(name: "Al Safa Park 2", distanceMeters: 2, areaSquareMeters: 15_000)
            with
        { Id = "osm_top" };
        var aiPick = Candidate(name: "Al Safa Park 2 (Actual Boundary)", distanceMeters: 40, areaSquareMeters: 22_000)
            with
        { Id = "osm_ai_pick" };
        _candidateProvider.SearchAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<BoundaryCandidate> { topRanked, aiPick });
        _satelliteImageProvider.FetchAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(SampleImage);
        _visionAnalyzer.AnalyzeAsync(SampleImage, Arg.Any<IReadOnlyList<ScoredBoundaryCandidate>>(), "Al Safa Park 2", Arg.Any<GeoPoint>(), Arg.Any<CancellationToken>())
            .Returns(new BoundaryVisionAnalysis(AiUsed: true, "osm_ai_pick", 0.88, "high", ["Visible fence line matches this candidate."], [], false));

        var outcome = await _service.ResolveAsync(AlSafaLocation, ChatId);

        outcome.Type.Should().Be(BoundaryResolutionOutcomeType.Confirmed);
        outcome.ConfirmedBoundary!.AreaSquareMeters.Should().Be(22_000);
        outcome.ConfirmationText.Should().Contain("Gemini's pick");
    }

    [Fact]
    public async Task ResolveAsync_ShouldFallBackToDeterministicScoreSilently_WhenNoSatelliteImageIsAvailable()
    {
        _candidateProvider.SearchAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<BoundaryCandidate> { Candidate() });
        _satelliteImageProvider.FetchAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((SatelliteImage?)null);

        var outcome = await _service.ResolveAsync(AlSafaLocation, ChatId);

        outcome.Type.Should().Be(BoundaryResolutionOutcomeType.Confirmed);
        outcome.ConfirmedBoundary!.ConfidenceLevel.Should().Be(BoundaryConfidenceLevel.High);
        outcome.ConfirmationText.Should().NotContain("Gemini");
        _ = _visionAnalyzer.DidNotReceive().AnalyzeAsync(
            Arg.Any<SatelliteImage>(), Arg.Any<IReadOnlyList<ScoredBoundaryCandidate>>(), Arg.Any<string>(), Arg.Any<GeoPoint>(), Arg.Any<CancellationToken>());
    }

    // specs/042-site-boundary-resolution post-release follow-up — user-requested vision-based
    // geometry correction: a single-candidate site can have nothing else to "pick" even when its
    // own OSM geometry is positionally wrong, so Gemini's own visual read of the boundary
    // (already geo-referenced by IBoundaryVisionAnalyzer) can override the mapped geometry —
    // but only after a plausibility check (area ratio + centroid distance), never at face value.
    private static List<GeoPoint> ShiftedSamplePolygon(double latOffsetDegrees, double lonOffsetDegrees) =>
        SamplePolygon.ExteriorRing.Select(p => new GeoPoint(p.Latitude + latOffsetDegrees, p.Longitude + lonOffsetDegrees)).ToList();

    private static BoundaryVisionAnalysis VisionWithObservedBoundary(IReadOnlyList<GeoPoint> observedBoundary) => new(
        AiUsed: true, SelectedCandidateId: "osm_1", Confidence: 0.85, BoundaryQuality: "high",
        Reasoning: ["Matches visible fence line."], Issues: [], RequiresRefinement: false, ObservedBoundary: observedBoundary);

    [Fact]
    public async Task ResolveAsync_ShouldUseGeminisObservedGeometry_WhenItPassesThePlausibilityCheck()
    {
        _candidateProvider.SearchAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<BoundaryCandidate> { Candidate() });
        _satelliteImageProvider.FetchAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(SampleImage);
        var observed = ShiftedSamplePolygon(0.0002, 0.0002); // ~20-30m shift — same shape/area, different position
        _visionAnalyzer.AnalyzeAsync(SampleImage, Arg.Any<IReadOnlyList<ScoredBoundaryCandidate>>(), "Al Safa Park 2", Arg.Any<GeoPoint>(), Arg.Any<CancellationToken>())
            .Returns(VisionWithObservedBoundary(observed));

        var outcome = await _service.ResolveAsync(AlSafaLocation, ChatId);

        outcome.Type.Should().Be(BoundaryResolutionOutcomeType.Confirmed);
        outcome.ConfirmedBoundary!.Source.Should().Be(SiteBoundarySource.AiInterpretation);
        outcome.ConfirmedBoundary.Polygon.Should().BeEquivalentTo(observed, opts => opts.WithStrictOrdering());
        outcome.ConfirmationText.Should().Contain("Gemini's own visual read");
    }

    [Fact]
    public async Task ResolveAsync_ShouldKeepMappedGeometry_WhenObservedAreaIsImplausiblySmall()
    {
        _candidateProvider.SearchAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<BoundaryCandidate> { Candidate(areaSquareMeters: 15_000) });
        _satelliteImageProvider.FetchAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(SampleImage);
        // A tiny sliver (roughly 1m x 1m) — area ratio against 15,000 m2 is far below the 0.3x floor.
        var tinyObserved = new List<GeoPoint>
        {
            new(25.1560, 55.2218), new(25.15601, 55.2218), new(25.15601, 55.22181), new(25.1560, 55.22181),
        };
        _visionAnalyzer.AnalyzeAsync(SampleImage, Arg.Any<IReadOnlyList<ScoredBoundaryCandidate>>(), "Al Safa Park 2", Arg.Any<GeoPoint>(), Arg.Any<CancellationToken>())
            .Returns(VisionWithObservedBoundary(tinyObserved));

        var outcome = await _service.ResolveAsync(AlSafaLocation, ChatId);

        outcome.ConfirmedBoundary!.Source.Should().Be(SiteBoundarySource.OsmBoundary);
        outcome.ConfirmedBoundary.Polygon.Should().BeEquivalentTo(SamplePolygon.ExteriorRing, opts => opts.WithStrictOrdering());
    }

    [Fact]
    public async Task ResolveAsync_ShouldKeepMappedGeometry_WhenObservedCentroidIsFarOutsideTheSearchRadius()
    {
        _candidateProvider.SearchAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<BoundaryCandidate> { Candidate() });
        _satelliteImageProvider.FetchAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(SampleImage);
        // Same shape/area as the mapped candidate, but shifted ~2 degrees (~200km) away — far
        // outside SearchRadiusMeters (500m default), so this can't plausibly be the same feature.
        var farAwayObserved = ShiftedSamplePolygon(2.0, 2.0);
        _visionAnalyzer.AnalyzeAsync(SampleImage, Arg.Any<IReadOnlyList<ScoredBoundaryCandidate>>(), "Al Safa Park 2", Arg.Any<GeoPoint>(), Arg.Any<CancellationToken>())
            .Returns(VisionWithObservedBoundary(farAwayObserved));

        var outcome = await _service.ResolveAsync(AlSafaLocation, ChatId);

        outcome.ConfirmedBoundary!.Source.Should().Be(SiteBoundarySource.OsmBoundary);
        outcome.ConfirmedBoundary.Polygon.Should().BeEquivalentTo(SamplePolygon.ExteriorRing, opts => opts.WithStrictOrdering());
    }

    // specs/043 US5 (FR-032/FR-033) — Gemini vision is an enhancement, never a single point of
    // failure. Whatever reason it is unavailable for, the deterministic result still stands.
    [Theory]
    [InlineData("Gemini vision request failed (429).")]
    [InlineData("Gemini vision request failed (403).")]
    [InlineData("Gemini vision analysis timed out after 30s.")]
    [InlineData("Google Gemini has no credential configured — an administrator must set one to enable AI vision verification.")]
    [InlineData("Gemini vision analysis failed: JsonException.")]
    public async Task ResolveAsync_ShouldStillConfirmTheDeterministicBoundary_WhenVisionIsUnavailable(string reason)
    {
        var winner = Candidate(name: "Al Safa Park 2", distanceMeters: 5);
        _candidateProvider.SearchAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<BoundaryCandidate> { winner });
        _satelliteImageProvider.FetchAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(SampleImage);
        _visionAnalyzer.AnalyzeAsync(SampleImage, Arg.Any<IReadOnlyList<ScoredBoundaryCandidate>>(), "Al Safa Park 2", Arg.Any<GeoPoint>(), Arg.Any<CancellationToken>())
            .Returns(BoundaryVisionAnalysis.NotConfigured(reason));

        var outcome = await _service.ResolveAsync(AlSafaLocation, ChatId);

        outcome.Type.Should().Be(BoundaryResolutionOutcomeType.Confirmed);
        outcome.ConfirmedBoundary!.Source.Should().Be(SiteBoundarySource.OsmBoundary);
        // FR-033: and it must not claim an AI-verified confidence it never obtained.
        outcome.ConfirmationText.Should().NotContain("Gemini vision analysis");
    }
}
