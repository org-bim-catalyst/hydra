using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AskLucy.Application.Tests.SiteBoundaries;

/// <summary>
/// specs/042-site-boundary-resolution T020 — <see cref="BoundaryCandidateScorer"/> weighs every
/// factor from the reference notebook's <c>score_candidate</c> port, including FR-013's
/// implausible-size penalty.
/// </summary>
public sealed class BoundaryCandidateScorerTests
{
    private static readonly SiteBoundaryPolygon SamplePolygon = new([
        new GeoPoint(25.1560, 55.2210), new GeoPoint(25.1560, 55.2220),
        new GeoPoint(25.1550, 55.2220), new GeoPoint(25.1550, 55.2210), new GeoPoint(25.1560, 55.2210),
    ]);

    private readonly BoundaryScoringOptions _options = new();
    private BoundaryCandidateScorer CreateScorer() => new(Microsoft.Extensions.Options.Options.Create(_options));

    private static BoundaryCandidate Candidate(
        SiteBoundarySource source = SiteBoundarySource.OsmBoundary, string name = "Al Safa Park 2",
        IReadOnlyDictionary<string, string>? tags = null, double distanceMeters = 10, double areaSquareMeters = 15_000) =>
        new("osm_1", SamplePolygon, source, name, tags ?? new Dictionary<string, string> { ["leisure"] = "park" }, distanceMeters, areaSquareMeters);

    [Fact]
    public void ScoreAll_ShouldRankExactNameMatchHigherThanNoMatch()
    {
        var scorer = CreateScorer();
        var goodMatch = Candidate(name: "Al Safa Park 2");
        var noMatch = Candidate(name: "Some Unrelated Building");

        var ranked = scorer.ScoreAll([noMatch, goodMatch], "Al Safa Park 2");

        ranked[0].Candidate.Name.Should().Be("Al Safa Park 2");
        ranked[0].ScoreBreakdown["name_match"].Should().Be(1.0);
        ranked[1].ScoreBreakdown["name_match"].Should().Be(0.0);
    }

    [Fact]
    public void ScoreAll_ShouldPreferGovernmentCadastralOverOsmBoundary()
    {
        var scorer = CreateScorer();
        var osm = Candidate(source: SiteBoundarySource.OsmBoundary, name: "Site", distanceMeters: 5);
        var cadastral = Candidate(source: SiteBoundarySource.GovernmentCadastral, name: "Site", distanceMeters: 5);

        var ranked = scorer.ScoreAll([osm, cadastral], "Site");

        ranked[0].Candidate.Source.Should().Be(SiteBoundarySource.GovernmentCadastral);
    }

    [Theory]
    [InlineData(10, 0.2)]      // FR-013: implausibly small (<50 m2) penalized
    [InlineData(1_000_000, 0.4)] // FR-013: implausibly large (>500,000 m2) penalized
    [InlineData(15_000, 1.0)]   // plausible site-scale area
    public void Score_ShouldApplyGeometryQualityPenalty_ForImplausibleAreas(double area, double expectedGeometryScore)
    {
        var scorer = CreateScorer();
        var candidate = Candidate(areaSquareMeters: area);

        var scored = scorer.ScoreAll([candidate], "Al Safa Park 2");

        scored[0].ScoreBreakdown["geometry_quality"].Should().Be(expectedGeometryScore);
    }

    [Fact]
    public void Score_ShouldDecayCenterProximity_AsDistanceApproachesSearchRadius()
    {
        var scorer = CreateScorer();
        var atCenter = Candidate(distanceMeters: 0);
        var atRadiusEdge = Candidate(distanceMeters: _options.SearchRadiusMeters);

        var scoredAtCenter = scorer.ScoreAll([atCenter], "Al Safa Park 2")[0];
        var scoredAtEdge = scorer.ScoreAll([atRadiusEdge], "Al Safa Park 2")[0];

        scoredAtCenter.ScoreBreakdown["center_proximity"].Should().Be(1.0);
        scoredAtEdge.ScoreBreakdown["center_proximity"].Should().Be(0.0);
    }

    [Fact]
    public void Score_ShouldRewardLandUseTagAgreement()
    {
        var scorer = CreateScorer();
        var withLeisureTag = Candidate(tags: new Dictionary<string, string> { ["leisure"] = "park" });
        var withoutRelevantTag = Candidate(tags: new Dictionary<string, string> { ["highway"] = "residential" });

        var scoredWith = scorer.ScoreAll([withLeisureTag], "Site")[0];
        var scoredWithout = scorer.ScoreAll([withoutRelevantTag], "Site")[0];

        scoredWith.ScoreBreakdown["landuse_agreement"].Should().Be(1.0);
        scoredWithout.ScoreBreakdown["landuse_agreement"].Should().Be(0.3);
    }

    [Fact]
    public void ScoreAll_ShouldOrderDescendingByCombinedScore()
    {
        var scorer = CreateScorer();
        var strong = Candidate(source: SiteBoundarySource.GovernmentCadastral, name: "Al Safa Park 2", distanceMeters: 0);
        var weak = Candidate(source: SiteBoundarySource.AiInterpretation, name: "Unrelated", distanceMeters: 490, tags: new Dictionary<string, string>());

        var ranked = scorer.ScoreAll([weak, strong], "Al Safa Park 2");

        ranked.Should().HaveCount(2);
        ranked[0].Score.Should().BeGreaterThan(ranked[1].Score);
        ranked[0].Candidate.Should().Be(strong);
    }
}
