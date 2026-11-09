using System.Runtime.CompilerServices;
using AskLucy.Application.Spatial;
using AskLucy.Application.Spatial.Query;
using AskLucy.Domain.SiteBoundaries;
using AskLucy.Infrastructure.Spatial.Overlay;
using FluentAssertions;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Spatial.Overlay;

public sealed class OverlayAnalysisProviderTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldReturnOnlyCandidatesMeetingRequiredConditions()
    {
        var parcel = Polygon("parcel-1", "available", 23.001, 58.001);
        var zone = Polygon("zone-1", "active", 23.001, 58.001);
        var dataProvider = new FakeSpatialQueryProvider(new Dictionary<string, IReadOnlyList<QueryResultFeature>>
        {
            ["parcels"] = [parcel],
            ["zones"] = [zone],
        });
        var provider = new OverlayAnalysisProvider([dataProvider]);
        var inputs = new[]
        {
            new OverlayResolvedInput(new OverlayInput("parcels", "parcels", OverlayInputRole.Candidate), Dataset("parcels")),
            new OverlayResolvedInput(new OverlayInput("zones", "zones", OverlayInputRole.Constraint), Dataset("zones")),
        };
        var definition = new OverlayAnalysisDefinition("op-1", "test area",
            new ResolvedStudyArea("test area", new GeoPoint(23, 58), 1000), "parcels", inputs,
            [new OverlayStep("eligible", OverlayOperationKind.SpatialIntersection,
                ["parcels", "zones"], "eligible", OverlaySpatialRelation.Intersects)],
            [new OverlayCriterion("available", OverlayCriterionKind.Attribute, "parcels", "equals",
                "status", Text: "available")], null);
        var context = new SpatialExecutionContext("user-1", Guid.NewGuid(), definition.StudyArea, null, []);

        var result = await provider.ExecuteAsync(definition, context, CancellationToken.None);

        result.FailureReason.Should().BeNull();
        result.Layers.Should().HaveCount(3);
        result.Layers.Single(layer => layer.Role == "result").Features.Should().ContainSingle()
            .Which.FeatureId.Should().Be("parcel-1");
        result.CandidateCount.Should().Be(1);
        result.ExcludedCount.Should().Be(0);
        result.Coverage.Should().HaveCount(2);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFailVisiblyWhenADatasetHasNoRegisteredProvider()
    {
        var provider = new OverlayAnalysisProvider([]);
        var input = new OverlayResolvedInput(new OverlayInput("parcels", "parcels", OverlayInputRole.Candidate), Dataset("parcels"));
        var definition = new OverlayAnalysisDefinition("op-1", "area",
            new ResolvedStudyArea("area", new GeoPoint(23, 58), 1000), "parcels", [input], [], [], null);
        var context = new SpatialExecutionContext("user-1", Guid.NewGuid(), definition.StudyArea, null, []);

        var result = await provider.ExecuteAsync(definition, context, CancellationToken.None);

        result.FailureReason.Should().Contain("source is not available");
        result.Layers.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReportPartialCoverageWhenInvalidFeaturesAreExcluded()
    {
        var valid = Polygon("valid", "available", 23.001, 58.001);
        var invalid = new QueryResultFeature("invalid", "Invalid",
            new FeatureGeometry(FeatureGeometryKind.Polygon,
            [new(23.001, 58.001), new(23.002, 58.002), new(23.001, 58.002), new(23.002, 58.001), new(23.001, 58.001)]),
            new Dictionary<string, object?> { ["status"] = "available" });
        var provider = new OverlayAnalysisProvider([new FakeSpatialQueryProvider(new Dictionary<string, IReadOnlyList<QueryResultFeature>>
        {
            ["parcels"] = [valid, invalid], ["zones"] = [Polygon("zone", "active", 23.001, 58.001)],
        })]);
        var inputs = new[]
        {
            new OverlayResolvedInput(new OverlayInput("parcels", "parcels", OverlayInputRole.Candidate), Dataset("parcels")),
            new OverlayResolvedInput(new OverlayInput("zones", "zones", OverlayInputRole.Constraint), Dataset("zones")),
        };
        var area = new ResolvedStudyArea("area", new GeoPoint(23, 58), 1000);
        var definition = new OverlayAnalysisDefinition("op", "area", area, "parcels", inputs,
            [new OverlayStep("selected", OverlayOperationKind.SpatialIntersection, ["parcels", "zones"], "selected", OverlaySpatialRelation.Intersects)],
            [new OverlayCriterion("available", OverlayCriterionKind.Attribute, "parcels", "eq", "status", Text: "available")], null);

        var result = await provider.ExecuteAsync(definition,
            new SpatialExecutionContext("u", Guid.NewGuid(), area, null, []), CancellationToken.None);

        result.Coverage.Should().Contain(source => source.CoverageStatus == "Partial");
        result.Warnings.Should().ContainSingle().Which.Should().Contain("invalid");
        result.Layers.Single(layer => layer.Role == "result").Features.Should().ContainSingle(feature => feature.FeatureId == "valid");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnVisibleFailureWhenAQueryProviderThrows()
    {
        var provider = new OverlayAnalysisProvider([new ThrowingSpatialQueryProvider()]);
        var input = new OverlayResolvedInput(new OverlayInput("parcels", "parcels", OverlayInputRole.Candidate), Dataset("parcels"));
        var area = new ResolvedStudyArea("area", new GeoPoint(23, 58), 1000);
        var definition = new OverlayAnalysisDefinition("op", "area", area, "parcels", [input], [], [], null);

        var result = await provider.ExecuteAsync(definition,
            new SpatialExecutionContext("u", Guid.NewGuid(), area, null, []), CancellationToken.None);

        result.FailureReason.Should().Contain("source failed");
        result.Layers.Should().BeEmpty();
    }

    private static DatasetEntry Dataset(string id) => new(id, id,
        [new AttributeField("status", "Status", AttributeFieldType.String, null, "available")],
        [SpatialRelationship.Intersecting, SpatialRelationship.Near], "fixture");

    private static QueryResultFeature Polygon(string id, string status, double latitude, double longitude) => new(
        id, id,
        new FeatureGeometry(FeatureGeometryKind.Polygon,
        [new(latitude, longitude), new(latitude, longitude + 0.001), new(latitude + 0.001, longitude + 0.001),
         new(latitude + 0.001, longitude), new(latitude, longitude)]),
        new Dictionary<string, object?> { ["status"] = status });

    private sealed class FakeSpatialQueryProvider(
        IReadOnlyDictionary<string, IReadOnlyList<QueryResultFeature>> featuresByDataset) : ISpatialQueryProvider
    {
        public IReadOnlyList<string> SupportedDatasetIds => [.. featuresByDataset.Keys];

        public async IAsyncEnumerable<QueryResultBatch> ExecuteAsync(
            SpatialQueryDefinition query,
            SpatialExecutionContext context,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return new QueryResultBatch(0, featuresByDataset[query.Dataset.Id],
                featuresByDataset[query.Dataset.Id].Count, featuresByDataset[query.Dataset.Id].Count, true);
        }
    }

    private sealed class ThrowingSpatialQueryProvider : ISpatialQueryProvider
    {
        public IReadOnlyList<string> SupportedDatasetIds => ["parcels"];

        public async IAsyncEnumerable<QueryResultBatch> ExecuteAsync(
            SpatialQueryDefinition query,
            SpatialExecutionContext context,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            throw new InvalidOperationException("upstream unavailable");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
    }
}
