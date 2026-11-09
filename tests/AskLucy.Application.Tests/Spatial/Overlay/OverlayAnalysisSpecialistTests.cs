using AskLucy.Application.Spatial;
using AskLucy.Application.Spatial.Query;
using AskLucy.Application.Spatial.Specialists;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Spatial.Overlay;

public sealed class OverlayAnalysisSpecialistTests
{
    private static OverlayAnalysisParameters MinimalParameters() => new("parcels",
        [new OverlayInput("parcels", "land-parcels", OverlayInputRole.Candidate),
         new OverlayInput("zones", "development-areas", OverlayInputRole.Constraint)],
        [new OverlayStep("eligible", OverlayOperationKind.Intersection, ["parcels", "zones"], "eligible")],
        [new OverlayCriterion("area", OverlayCriterionKind.Area, "parcels", "gt", Number: 1, Unit: "m2")]);

    private static SpatialOperation Operation(OverlayAnalysisParameters parameters) => new(
        "op-1", SpatialOperationKinds.OverlayAnalysis, [], null,
        new StudyAreaReference(StudyAreaKind.NamedArea, "Muscat"), OverlayParameters: parameters);

    private static SpatialExecutionContext Context() => new("user-1", Guid.NewGuid(),
        new ResolvedStudyArea("Muscat", new GeoPoint(23.5, 58.3), 1000), null, []);

    [Fact]
    public async Task ExecuteAsync_ShouldReturnRankedLayerAndCoverageMetadata()
    {
        var catalog = Substitute.For<IDatasetCatalog>();
        var dataset = new DatasetEntry("land-parcels", "Land Parcels",
            [new AttributeField("access", "Access", AttributeFieldType.Numeric)], [], "fixture");
        var zoning = new DatasetEntry("zoning", "Zoning", [], [], "fixture");
        catalog.TryGet(Arg.Any<string>(), out Arg.Any<DatasetEntry>()).Returns(call =>
        {
            var found = call.ArgAt<string>(0) == dataset.Id ? dataset : zoning;
            call[1] = found;
            return call.ArgAt<string>(0) is "land-parcels" or "zoning";
        });
        var features = new[]
        {
            new QueryResultFeature("parcel-1", "Parcel 1",
                new FeatureGeometry(FeatureGeometryKind.Point, [new GeoPoint(23.5, 58.3)]),
                new Dictionary<string, object?> { ["access"] = 0.8 }),
        };
        var provider = Substitute.For<IOverlayAnalysisProvider>();
        provider.ExecuteAsync(Arg.Any<OverlayAnalysisDefinition>(), Arg.Any<SpatialExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(new OverlayAnalysisProviderResult(
                [new OverlayOutputLayer("eligible", "Suitable locations", "result", "intersection", features)],
                1, 0, [DataSourceCoverage.Complete("land-parcels", "Land Parcels")], [], null));
        var specialist = new OverlayAnalysisSpecialist(catalog, provider,
            new OverlayAnalysisAuditLogger(NullLogger<OverlayAnalysisAuditLogger>.Instance));
        var parameters = new OverlayAnalysisParameters("parcels",
            [new OverlayInput("parcels", "land-parcels", OverlayInputRole.Candidate),
             new OverlayInput("zones", "zoning", OverlayInputRole.Constraint)],
            [new OverlayStep("eligible", OverlayOperationKind.Intersection, ["parcels", "zones"], "eligible")],
            [new OverlayCriterion("access", OverlayCriterionKind.Attribute, "parcels", "gt", "access",
                Required: false, Direction: OverlayCriterionDirection.HigherIsBetter)],
            new OverlayScoringProfile([new OverlayScoreWeight("access", 1)]));
        var operation = new SpatialOperation("op-1", SpatialOperationKinds.OverlayAnalysis, [], null,
            OverlayParameters: parameters);
        var context = new SpatialExecutionContext("user-1", Guid.NewGuid(),
            new ResolvedStudyArea("area", new GeoPoint(23.5, 58.3), 1000), null, []);

        var result = await specialist.ExecuteAsync(operation, context, CancellationToken.None);

        result.Status.Should().Be(SpatialOperationStatus.Succeeded, result.FailureReason);
        result.Layers.Should().ContainSingle();
        result.Layers[0].Features![0].Overlay!.Rank.Should().Be(1);
        result.Layers[0].Overlay!.ScoreMeaning.Should().Contain("higher values rank higher");
        result.Layers[0].Overlay!.Coverage.Should().ContainSingle("Land Parcels: Complete");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnAVisibleFailureForUnavailableSource()
    {
        var catalog = new DatasetCatalog([DatasetCatalog.LandParcels]);
        var provider = Substitute.For<IOverlayAnalysisProvider>();
        var specialist = new OverlayAnalysisSpecialist(catalog, provider,
            new OverlayAnalysisAuditLogger(NullLogger<OverlayAnalysisAuditLogger>.Instance));

        var result = await specialist.ExecuteAsync(Operation(MinimalParameters()), Context(), CancellationToken.None);

        result.Status.Should().Be(SpatialOperationStatus.Failed);
        result.FailureReason.Should().Contain("not available");
        await provider.DidNotReceive().ExecuteAsync(Arg.Any<OverlayAnalysisDefinition>(), Arg.Any<SpatialExecutionContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPropagateCancellationInsteadOfReportingAnEmptyResult()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var catalog = new DatasetCatalog([DatasetCatalog.LandParcels, DatasetCatalog.DevelopmentAreas]);
        var provider = Substitute.For<IOverlayAnalysisProvider>();
        provider.ExecuteAsync(Arg.Any<OverlayAnalysisDefinition>(), Arg.Any<SpatialExecutionContext>(), cancellation.Token)
            .Returns(_ => Task.FromCanceled<OverlayAnalysisProviderResult>(cancellation.Token));
        var specialist = new OverlayAnalysisSpecialist(catalog, provider,
            new OverlayAnalysisAuditLogger(NullLogger<OverlayAnalysisAuditLogger>.Instance));

        var action = () => specialist.ExecuteAsync(Operation(MinimalParameters()), Context(), cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }
}
