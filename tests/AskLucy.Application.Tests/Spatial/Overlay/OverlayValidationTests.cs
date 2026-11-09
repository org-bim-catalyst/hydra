using AskLucy.Application.Spatial;
using AskLucy.Application.Spatial.Query;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Spatial.Overlay;

public sealed class OverlayValidationTests
{
    [Fact]
    public void Validate_ShouldRejectAForwardReferenceThatWouldCreateACyclicStepDependency()
    {
        var parameters = new OverlayAnalysisParameters("parcels",
            [new OverlayInput("parcels", "land-parcels", OverlayInputRole.Candidate),
             new OverlayInput("zones", "development-areas", OverlayInputRole.Constraint)],
            [new OverlayStep("first", OverlayOperationKind.Intersection, ["second", "zones"], "first-output"),
             new OverlayStep("second", OverlayOperationKind.Intersection, ["parcels", "zones"], "second")],
            [new OverlayCriterion("area", OverlayCriterionKind.Area, "first-output", "gt", Number: 1, Unit: "m2")]);
        var operation = new SpatialOperation("overlay", SpatialOperationKinds.OverlayAnalysis, [], null,
            new StudyAreaReference(StudyAreaKind.NamedArea, "Muscat"), OverlayParameters: parameters);

        var result = new SpatialBundleValidator().Validate(SpatialRequestBundle.Create("bundle", [operation]));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ShouldRejectARequiredScoringCriterionWithoutAComparableDirection()
    {
        var parameters = new OverlayAnalysisParameters("parcels",
            [new OverlayInput("parcels", "land-parcels", OverlayInputRole.Candidate),
             new OverlayInput("zones", "development-areas", OverlayInputRole.Constraint)],
            [new OverlayStep("step", OverlayOperationKind.Intersection, ["parcels", "zones"], "result")],
            [new OverlayCriterion("area", OverlayCriterionKind.Area, "parcels", "gt", Number: 1, Unit: "m2")],
            new OverlayScoringProfile([new OverlayScoreWeight("area", 1)]));
        var operation = new SpatialOperation("overlay", SpatialOperationKinds.OverlayAnalysis, [], null,
            new StudyAreaReference(StudyAreaKind.NamedArea, "Muscat"), OverlayParameters: parameters);

        var result = new SpatialBundleValidator().Validate(SpatialRequestBundle.Create("bundle", [operation]));

        result.IsValid.Should().BeFalse();
    }
}
