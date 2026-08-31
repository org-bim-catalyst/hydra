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
    public async Task ResolveAsync_ShouldRepositionTheMappedGeometry_RatherThanReplaceItWithGeminisOutline()
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
        // Same shift, so the corrected outline lands on the observed one — but it got there by
        // translating the mapped ring, not by adopting Gemini's trace.
        outcome.ConfirmedBoundary.Polygon.Should().BeEquivalentTo(
            observed, opts => opts.WithStrictOrdering().Using<double>(
                ctx => ctx.Subject.Should().BeApproximately(ctx.Expectation, 1e-9)).WhenTypeIs<double>());
        outcome.ConfirmationText.Should().Contain("repositioned");
    }

    [Fact]
    public async Task ResolveAsync_ShouldPreserveEveryMappedVertex_WhenGeminiTracesACruderOutline()
    {
        // The live failure this replaced: OSM's way for Al Safa Park 2 carries seven distinct
        // corners including a stepped notch on one side, and Gemini traces it as a four-point
        // approximation. Substituting the trace fixed the offset and destroyed the shape.
        var mapped = new List<GeoPoint>
        {
            new(25.1548445, 55.2218037),
            new(25.1553063, 55.2210997),
            new(25.1562712, 55.2218513),
            new(25.1563532, 55.2219158), // the stepped corner — three vertices where a
            new(25.1565255, 55.2220514), // rectangle would have one
            new(25.1560252, 55.2227775),
            new(25.1551806, 55.2220769),
            new(25.1548445, 55.2218037),
        };
        var crudeTrace = new List<GeoPoint>
        {
            new(25.1550, 55.2213), new(25.1565, 55.2222), new(25.1560, 55.2229), new(25.1550, 55.2213),
        };

        _candidateProvider.SearchAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<BoundaryCandidate>
            {
                new("osm_1", new SiteBoundaryPolygon(mapped), SiteBoundarySource.OsmBoundary, "Al Safa Park 2",
                    new Dictionary<string, string> { ["leisure"] = "park" }, mapped.Count,
                    GeometryMath.AreaSquareMeters(mapped)),
            });
        _satelliteImageProvider.FetchAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(SampleImage);
        _visionAnalyzer.AnalyzeAsync(SampleImage, Arg.Any<IReadOnlyList<ScoredBoundaryCandidate>>(),
                Arg.Any<string>(), Arg.Any<GeoPoint>(), Arg.Any<CancellationToken>())
            .Returns(VisionWithObservedBoundary(crudeTrace));

        var outcome = await _service.ResolveAsync(AlSafaLocation, ChatId, TestContext.Current.CancellationToken);

        var result = outcome.ConfirmedBoundary!.Polygon;
        result.Should().HaveCount(mapped.Count, "every mapped vertex survives a repositioning");

        // Rigid translation: every edge keeps its length, so the notch is still a notch.
        for (var i = 1; i < mapped.Count; i++)
        {
            GeometryMath.DistanceMeters(result[i - 1], result[i])
                .Should().BeApproximately(GeometryMath.DistanceMeters(mapped[i - 1], mapped[i]), 0.5);
        }
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

    // -------------------------------------------------------------------------------------------
    // specs/044-location-viewer-regression — US3: Gemini Vision must stay STRICTLY ADDITIVE.
    // Vision may improve a boundary; it may never remove one. Until T020 the imagery/analysis calls
    // sat outside any try/catch, so an escaped exception discarded a perfectly good
    // deterministically-scored OSM boundary — and, before the handler fix, the whole chat turn.
    // -------------------------------------------------------------------------------------------

    public static TheoryData<Exception> VisionFailures() =>
    [
        new HttpRequestException("imagery host unreachable"),
        new InvalidOperationException("bad state"),
        new System.Text.Json.JsonException("unparseable vision payload"),
    ];

    /// <summary>specs/044 T021 (FR-004, contract B-3) — the imagery fetch throws.</summary>
    [Theory]
    [MemberData(nameof(VisionFailures))]
    public async Task ResolveAsync_ShouldKeepTheDeterministicBoundary_WhenSatelliteImageryThrows(Exception failure)
    {
        _options.EnableAiVisionVerification = true;
        _candidateProvider.SearchAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<BoundaryCandidate> { Candidate() });
        _satelliteImageProvider.FetchAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<SatelliteImage?>(_ => throw failure);

        var outcome = await _service.ResolveAsync(AlSafaLocation, ChatId, TestContext.Current.CancellationToken);

        outcome.Type.Should().Be(BoundaryResolutionOutcomeType.Confirmed);
        outcome.ConfirmedBoundary!.Source.Should().Be(SiteBoundarySource.OsmBoundary);
        outcome.ConfirmedBoundary.Polygon.Should().BeEquivalentTo(SamplePolygon.ExteriorRing);
    }

    /// <summary>specs/044 T021 (FR-004, contract B-3) — the vision analyzer itself throws.</summary>
    [Theory]
    [MemberData(nameof(VisionFailures))]
    public async Task ResolveAsync_ShouldKeepTheDeterministicBoundary_WhenVisionAnalysisThrows(Exception failure)
    {
        _options.EnableAiVisionVerification = true;
        _candidateProvider.SearchAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<BoundaryCandidate> { Candidate() });
        _satelliteImageProvider.FetchAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(SampleImage);
        _visionAnalyzer.AnalyzeAsync(Arg.Any<SatelliteImage>(), Arg.Any<IReadOnlyList<ScoredBoundaryCandidate>>(),
                Arg.Any<string>(), Arg.Any<GeoPoint>(), Arg.Any<CancellationToken>())
            .Returns<BoundaryVisionAnalysis>(_ => throw failure);

        var outcome = await _service.ResolveAsync(AlSafaLocation, ChatId, TestContext.Current.CancellationToken);

        outcome.Type.Should().Be(BoundaryResolutionOutcomeType.Confirmed);
        outcome.ConfirmedBoundary!.Source.Should().Be(SiteBoundarySource.OsmBoundary);
    }

    /// <summary>
    /// specs/044 T021 (contract B-2) — cancellation is the ONE exception this service may surface.
    /// Swallowing it here would strip the caller's ability to tell a user cancellation from a
    /// budget expiry (contract H-3).
    /// </summary>
    [Fact]
    public async Task ResolveAsync_ShouldPropagateCancellation_RatherThanDegradingLikeAVisionFailure()
    {
        _options.EnableAiVisionVerification = true;
        _candidateProvider.SearchAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<BoundaryCandidate> { Candidate() });
        _satelliteImageProvider.FetchAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(SampleImage);
        _visionAnalyzer.AnalyzeAsync(Arg.Any<SatelliteImage>(), Arg.Any<IReadOnlyList<ScoredBoundaryCandidate>>(),
                Arg.Any<string>(), Arg.Any<GeoPoint>(), Arg.Any<CancellationToken>())
            .Returns<BoundaryVisionAnalysis>(_ => throw new OperationCanceledException());

        var act = async () => await _service.ResolveAsync(AlSafaLocation, ChatId, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// specs/044 T022 (FR-008, SC-005) — the regression guard for 9b19695. The point of hardening
    /// vision is that it must still DO its job: a plausible observed boundary still overrides
    /// positionally-wrong OSM geometry. A fix that quietly disabled this would pass every other
    /// test in this file.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_ShouldStillApplyVisionGeometryCorrection_WhenTheObservedBoundaryIsPlausible()
    {
        _options.EnableAiVisionVerification = true;
        _candidateProvider.SearchAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<BoundaryCandidate> { Candidate() });
        _satelliteImageProvider.FetchAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(SampleImage);

        // Same shape and size as the mapped candidate but nudged — the positional-offset case.
        var observed = new List<GeoPoint>
        {
            new(25.1561, 55.2211), new(25.1561, 55.2221),
            new(25.1551, 55.2221), new(25.1551, 55.2211), new(25.1561, 55.2211),
        };

        _visionAnalyzer.AnalyzeAsync(Arg.Any<SatelliteImage>(), Arg.Any<IReadOnlyList<ScoredBoundaryCandidate>>(),
                Arg.Any<string>(), Arg.Any<GeoPoint>(), Arg.Any<CancellationToken>())
            .Returns(new BoundaryVisionAnalysis(
                AiUsed: true, SelectedCandidateId: "osm_1", Confidence: 0.9, BoundaryQuality: "high",
                Reasoning: [], Issues: [], RequiresRefinement: false, ObservedBoundary: observed));

        var outcome = await _service.ResolveAsync(AlSafaLocation, ChatId, TestContext.Current.CancellationToken);

        outcome.Type.Should().Be(BoundaryResolutionOutcomeType.Confirmed);
        outcome.ConfirmedBoundary!.Source.Should().Be(SiteBoundarySource.AiInterpretation);
        outcome.ConfirmedBoundary.Polygon.Should().BeEquivalentTo(
            observed, opts => opts.WithStrictOrdering().Using<double>(
                ctx => ctx.Subject.Should().BeApproximately(ctx.Expectation, 1e-9)).WhenTypeIs<double>());
        outcome.ConfirmationText.Should().Contain("repositioned");
    }

    /// <summary>
    /// specs/044 T022 (FR-008) — and the plausibility gate still rejects an implausible read,
    /// keeping the mapped geometry. Vision is trusted conditionally, never at face value.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_ShouldStillRejectAnImplausibleObservedBoundary_AndKeepTheMappedGeometry()
    {
        _options.EnableAiVisionVerification = true;
        _candidateProvider.SearchAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<BoundaryCandidate> { Candidate() });
        _satelliteImageProvider.FetchAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(SampleImage);

        // Far too large relative to the mapped candidate — outside the 0.3x–3.0x area tolerance.
        var implausible = new List<GeoPoint>
        {
            new(25.2000, 55.1000), new(25.2000, 55.4000),
            new(25.0000, 55.4000), new(25.0000, 55.1000), new(25.2000, 55.1000),
        };

        _visionAnalyzer.AnalyzeAsync(Arg.Any<SatelliteImage>(), Arg.Any<IReadOnlyList<ScoredBoundaryCandidate>>(),
                Arg.Any<string>(), Arg.Any<GeoPoint>(), Arg.Any<CancellationToken>())
            .Returns(new BoundaryVisionAnalysis(
                AiUsed: true, SelectedCandidateId: "osm_1", Confidence: 0.9, BoundaryQuality: "low",
                Reasoning: [], Issues: [], RequiresRefinement: false, ObservedBoundary: implausible));

        var outcome = await _service.ResolveAsync(AlSafaLocation, ChatId, TestContext.Current.CancellationToken);

        outcome.ConfirmedBoundary!.Source.Should().Be(SiteBoundarySource.OsmBoundary);
        outcome.ConfirmedBoundary.Polygon.Should().BeEquivalentTo(SamplePolygon.ExteriorRing);
    }
}
