using AskLucy.Application.Spatial;
using AskLucy.Application.Spatial.Query;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Spatial.Overlay;

public sealed class OverlayScoringTests
{
    [Fact]
    public void Score_ShouldNormalizeDirectionsApplyWeightsAndRankDeterministically()
    {
        var candidates = new[]
        {
            Feature("parcel-a", 10, 100),
            Feature("parcel-b", 20, 200),
        };
        var criteria = new[]
        {
            new OverlayCriterion("population", OverlayCriterionKind.Attribute, "parcels", "greaterThan", "population",
                Required: false, Direction: OverlayCriterionDirection.HigherIsBetter),
            new OverlayCriterion("road-distance", OverlayCriterionKind.Distance, "roads", "within", Number: 500,
                Unit: "m", Required: false, Direction: OverlayCriterionDirection.LowerIsBetter),
        };
        var profile = new OverlayScoringProfile([
            new OverlayScoreWeight("population", 0.6),
            new OverlayScoreWeight("road-distance", 0.4),
        ]);

        var result = OverlayScorer.Score(candidates, criteria, profile);

        result[0].FeatureId.Should().Be("parcel-b");
        result[0].Overlay!.Score.Should().BeApproximately(0.6, 0.0001);
        result[0].Overlay.Rank.Should().Be(1);
        result[1].Overlay!.Score.Should().BeApproximately(0.4, 0.0001);
        result[1].Overlay.Rank.Should().Be(2);
    }

    [Fact]
    public void Score_ShouldReturnCandidatesUnchangedWhenNoProfileWasRequested()
    {
        var candidate = Feature("parcel-a", 10, 100);
        OverlayScorer.Score([candidate], [], null).Should().ContainSingle().Which.Should().BeSameAs(candidate);
    }

    private static QueryResultFeature Feature(string id, double population, double distance) => new(
        id, id,
        new FeatureGeometry(FeatureGeometryKind.Point, [new(23, 58)]),
        new Dictionary<string, object?> { ["population"] = population, ["road-distance"] = distance });
}
