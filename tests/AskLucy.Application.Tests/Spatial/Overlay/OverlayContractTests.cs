using AskLucy.Application.Spatial;
using AskLucy.Application.Spatial.Query;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Spatial.Overlay;

public sealed class OverlayContractTests
{
    private readonly SpatialBundleValidator _validator = new();

    private static SpatialOperation Operation(OverlayAnalysisParameters parameters) => new(
        "overlay-1",
        SpatialOperationKinds.OverlayAnalysis,
        [new FeatureSelector(Category: "land-parcels")],
        StudyArea: new StudyAreaReference(StudyAreaKind.NamedArea, "Muscat"),
        OverlayParameters: parameters);

    private static OverlayAnalysisParameters ValidParameters() => new(
        "parcels",
        [
            new OverlayInput("parcels", "land-parcels", OverlayInputRole.Candidate),
            new OverlayInput("zones", "development-areas", OverlayInputRole.Constraint),
        ],
        [new OverlayStep("eligible", OverlayOperationKind.Intersection, ["parcels", "zones"], "eligible")],
        [new OverlayCriterion("available", OverlayCriterionKind.Attribute, "parcels", "equals", "land-use", Text: "commercial")]);

    [Fact]
    public void Validate_ShouldAcceptAWellFormedBoundedOverlayOperation()
    {
        var result = _validator.Validate(SpatialRequestBundle.Create("bundle-1", [Operation(ValidParameters())]));

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors.Select(error => error.ErrorMessage)));
    }

    [Fact]
    public void Validate_ShouldRejectAnUnknownStepReference()
    {
        var parameters = ValidParameters() with
        {
            Steps = [new OverlayStep("eligible", OverlayOperationKind.Intersection, ["parcels", "missing"], "eligible")],
        };

        _validator.Validate(SpatialRequestBundle.Create("bundle-1", [Operation(parameters)]))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ShouldAllowAreaClarificationToRunBeforeAStudyAreaIsResolved()
    {
        var operation = Operation(ValidParameters()) with { StudyArea = null };

        var result = _validator.Validate(SpatialRequestBundle.Create("bundle-1", [operation]));

        result.IsValid.Should().BeTrue("the coordinator must be allowed to ask for a bounded area before dispatch");
    }

    [Fact]
    public void Validate_ShouldRejectNegativeOrZeroWeightProfiles()
    {
        var parameters = ValidParameters() with
        {
            Criteria = [ValidParameters().Criteria[0] with { Required = false, Direction = OverlayCriterionDirection.HigherIsBetter }],
            Scoring = new OverlayScoringProfile([new OverlayScoreWeight("available", 0)]),
        };

        _validator.Validate(SpatialRequestBundle.Create("bundle-1", [Operation(parameters)]))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void Normalize_ShouldPreserveValidOverlayFeaturesAndMetadata()
    {
        var feature = new QueryResultFeature("parcel-1", "Parcel 1",
            new FeatureGeometry(FeatureGeometryKind.Polygon,
            [new GeoPoint(23, 58), new GeoPoint(23, 58.001), new GeoPoint(23.001, 58.001),
             new GeoPoint(23, 58), new GeoPoint(23, 58)]),
            new Dictionary<string, object?> { ["area"] = 5000 },
            new OverlayFeatureMetadata(new Dictionary<string, bool> { ["eligible"] = true },
                new Dictionary<string, double> { ["population"] = 0.8 }, 0.8, 1, ["land-parcels:parcel-1"]));
        var draft = new SpatialLayerDraft("overlay-1", "overlay-1", "Suitable parcels", "Candidates",
            SpatialLayerKind.FeatureQuery, Features: [feature],
            Overlay: new OverlayLayerMetadata("result", "intersection", ["eligible"], ["land-parcels"]));

        var result = new SpatialResultNormalizer().Normalize("analysis-1", "bundle-1",
        [new SpatialSpecialistExecution(Operation(ValidParameters()), "overlay", new SpatialSpecialistOutcome(
            SpatialOperationStatus.Succeeded, [draft], []))]);

        result.Layers.Should().ContainSingle();
        result.Layers[0].Overlay!.Role.Should().Be("result");
        result.Layers[0].Features![0].Overlay!.Rank.Should().Be(1);
    }

    [Fact]
    public void Normalize_ShouldCountCandidatesWithoutCountingInputLayers()
    {
        var feature = new QueryResultFeature("parcel", "Parcel",
            new FeatureGeometry(FeatureGeometryKind.Point, [new GeoPoint(23, 58)]), new Dictionary<string, object?>());
        var input = new SpatialLayerDraft("source", "overlay-1", "Parcels", "Parcels",
            SpatialLayerKind.FeatureQuery, Features: [feature], Overlay: new OverlayLayerMetadata("input", "source", [], ["land-parcels"]));
        var result = new SpatialLayerDraft("result", "overlay-1", "Suitable", "Suitable",
            SpatialLayerKind.FeatureQuery, Features: [], Overlay: new OverlayLayerMetadata("result", "intersection", [], ["land-parcels"]));

        var normalized = new SpatialResultNormalizer().Normalize("analysis-1", "bundle-1",
            [new SpatialSpecialistExecution(Operation(ValidParameters()), "overlay",
                new SpatialSpecialistOutcome(SpatialOperationStatus.Succeeded, [input, result], []))]);

        normalized.Status.Should().Be(SpatialAnalysisStatus.Empty);
        normalized.ResultCount.Should().Be(0);
        normalized.Layers.Should().HaveCount(2);
    }
}
