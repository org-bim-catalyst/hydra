using AskLucy.Application.Locations;
using AskLucy.Application.Spatial;
using AskLucy.Application.Spatial.Query;
using AskLucy.Application.Spatial.Specialists;
using AskLucy.Domain.Chats;
using AskLucy.Domain.SiteBoundaries;
using Microsoft.Extensions.Logging.Abstractions;

namespace AskLucy.Application.Tests.Spatial.Query;

/// <summary>
/// specs/095 — unit tests for <see cref="SpatialQuerySpecialist"/>: empty result, provider
/// failure, attribute-only, spatial-only and combined queries, unknown dataset, and the
/// large-result warning.
/// </summary>
public class SpatialQuerySpecialistTests
{
    private static readonly SpatialExecutionContext TestContext = new(
        UserId: "test-user",
        UserChatId: Guid.NewGuid(),
        StudyArea: null,
        MapContext: null,
        ActiveLayers: []);

    private static SpatialQuerySpecialist CreateSpecialist(params QueryResultBatch[] batches) =>
        new(
            new DatasetCatalog([
                DatasetCatalog.Buildings,
                DatasetCatalog.Roads,
                DatasetCatalog.LandParcels,
                DatasetCatalog.DevelopmentAreas,
            ]),
            new NotFoundLocationService(),
            [new FakeQueryProvider(batches)],
            new SpatialQueryAuditLogger(NullLogger<SpatialQueryAuditLogger>.Instance));

    private static QueryResultFeature Feature(string id, double latitude = 23.5, double longitude = 58.3) =>
        new(
            FeatureId: id,
            Name: $"Feature {id}",
            Geometry: new FeatureGeometry(
                FeatureGeometryKind.Point,
                [new GeoPoint(latitude, longitude)]),
            Attributes: new Dictionary<string, object?>());

    private static SpatialOperation AttributeOnlyOperation() =>
        new(
            OperationId: "op-1",
            OperationKind: SpatialOperationKinds.SpatialQuery,
            Targets: [new FeatureSelector(Category: "buildings")],
            QueryParameters: new SpatialQueryParameters(
                DatasetId: "buildings",
                AttributePredicates:
                [
                    new AttributePredicate("height", AttributeOperator.Gt, 30),
                ]));

    private static SpatialOperation SpatialOnlyOperation() =>
        new(
            OperationId: "op-1",
            OperationKind: SpatialOperationKinds.SpatialQuery,
            Targets: [new FeatureSelector(Category: "buildings")],
            QueryParameters: new SpatialQueryParameters(
                DatasetId: "buildings",
                SpatialPredicates:
                [
                    new SpatialPredicate(
                        SpatialRelationship.Within,
                        SpatialReferenceKind.ExplicitGeometry,
                        ReferenceGeometry:
                        [
                            new GeoPoint(23.4, 58.2),
                            new GeoPoint(23.4, 58.4),
                            new GeoPoint(23.6, 58.4),
                            new GeoPoint(23.6, 58.2),
                        ]),
                ]));

    private static SpatialOperation CombinedOperation() =>
        new(
            OperationId: "op-1",
            OperationKind: SpatialOperationKinds.SpatialQuery,
            Targets: [new FeatureSelector(Category: "buildings")],
            QueryParameters: new SpatialQueryParameters(
                DatasetId: "buildings",
                AttributePredicates:
                [
                    new AttributePredicate("height", AttributeOperator.Gt, 30),
                ],
                SpatialPredicates:
                [
                    new SpatialPredicate(
                        SpatialRelationship.Within,
                        SpatialReferenceKind.ExplicitGeometry,
                        ReferenceGeometry:
                        [
                            new GeoPoint(23.4, 58.2),
                            new GeoPoint(23.4, 58.4),
                            new GeoPoint(23.6, 58.4),
                            new GeoPoint(23.6, 58.2),
                        ]),
                ]));

    [Fact]
    public async Task EmptyResult_ReturnsEmptyOutcome()
    {
        var specialist = CreateSpecialist(
            new QueryResultBatch(0, [], 0, 0, IsFinal: true));

        var outcome = await specialist.ExecuteAsync(
            AttributeOnlyOperation(), TestContext, CancellationToken.None);

        Assert.Equal(SpatialOperationStatus.Empty, outcome.Status);
        Assert.Empty(outcome.Layers);
    }

    [Fact]
    public async Task ProviderFailure_PropagatesException()
    {
        var specialist = CreateThrowingSpecialist();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            specialist.ExecuteAsync(AttributeOnlyOperation(), TestContext, CancellationToken.None));
    }

    [Fact]
    public async Task AttributeOnly_SucceedsWithFeatureLayer()
    {
        var specialist = CreateSpecialist(
            new QueryResultBatch(0, [Feature("b1")], 1, 1, IsFinal: true));

        var outcome = await specialist.ExecuteAsync(
            AttributeOnlyOperation(), TestContext, CancellationToken.None);

        Assert.Equal(SpatialOperationStatus.Succeeded, outcome.Status);
        Assert.Single(outcome.Layers);
    }

    [Fact]
    public async Task SpatialOnly_SucceedsWithFeatureLayer()
    {
        var specialist = CreateSpecialist(
            new QueryResultBatch(0, [Feature("b1")], 1, 1, IsFinal: true));

        var outcome = await specialist.ExecuteAsync(
            SpatialOnlyOperation(), TestContext, CancellationToken.None);

        Assert.Equal(SpatialOperationStatus.Succeeded, outcome.Status);
        Assert.Single(outcome.Layers);
    }

    [Fact]
    public async Task CombinedAttributeAndSpatial_Succeeds()
    {
        var specialist = CreateSpecialist(
            new QueryResultBatch(0, [Feature("b1"), Feature("b2")], 2, 2, IsFinal: true));

        var outcome = await specialist.ExecuteAsync(
            CombinedOperation(), TestContext, CancellationToken.None);

        Assert.Equal(SpatialOperationStatus.Succeeded, outcome.Status);
        Assert.Single(outcome.Layers);
    }

    [Fact]
    public async Task UnknownDataset_FailsWithClarification()
    {
        var specialist = CreateSpecialist();

        var operation = new SpatialOperation(
            OperationId: "op-1",
            OperationKind: SpatialOperationKinds.SpatialQuery,
            Targets: [new FeatureSelector(Category: "volcanoes")],
            QueryParameters: new SpatialQueryParameters(
                DatasetId: "volcanoes",
                AttributePredicates: [new AttributePredicate("height", AttributeOperator.Gt, 1)]));

        var outcome = await specialist.ExecuteAsync(
            operation, TestContext, CancellationToken.None);

        Assert.Equal(SpatialOperationStatus.Failed, outcome.Status);
        Assert.Contains("don't recognise", outcome.FailureReason);
    }

    [Fact]
    public async Task LargeResultSet_AddsWarning()
    {
        var features = Enumerable.Range(0, 2001).Select(i => Feature($"b{i}")).ToList();
        var specialist = CreateSpecialist(
            new QueryResultBatch(0, features, features.Count, features.Count, IsFinal: true));

        var outcome = await specialist.ExecuteAsync(
            AttributeOnlyOperation(), TestContext, CancellationToken.None);

        Assert.NotNull(outcome.Warnings);
        Assert.Contains(outcome.Warnings!, warning => warning.Contains("more than 2,000"));
    }

    private static SpatialQuerySpecialist CreateThrowingSpecialist() =>
        new(
            new DatasetCatalog([DatasetCatalog.Buildings]),
            new NotFoundLocationService(),
            [new ThrowingQueryProvider()],
            new SpatialQueryAuditLogger(NullLogger<SpatialQueryAuditLogger>.Instance));

    private sealed class FakeQueryProvider(params QueryResultBatch[] batches) : ISpatialQueryProvider
    {
        public IReadOnlyList<string> SupportedDatasetIds { get; } =
            ["buildings", "roads", "land-parcels", "development-areas"];

        public async IAsyncEnumerable<QueryResultBatch> ExecuteAsync(
            SpatialQueryDefinition query,
            SpatialExecutionContext context,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var batch in batches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return batch;
                await Task.Yield();
            }
        }
    }

    private sealed class ThrowingQueryProvider : ISpatialQueryProvider
    {
        public IReadOnlyList<string> SupportedDatasetIds { get; } = ["buildings"];

        public IAsyncEnumerable<QueryResultBatch> ExecuteAsync(
            SpatialQueryDefinition query,
            SpatialExecutionContext context,
            CancellationToken cancellationToken) => throw new InvalidOperationException("Provider error");
    }

    private sealed class NotFoundLocationService : ILocationResolutionService
    {
        public Task<LocationResolutionOutcome> ResolveAsync(
            string? userId,
            Guid userChatId,
            string latestUserMessage,
            ActiveSiteLocation? activeLocation,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new LocationResolutionOutcome(
                LocationResolutionOutcomeType.NotFound, null, null));

        public Task<LocationResolutionOutcome> ResolveQueryAsync(
            Guid userChatId, string query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LocationResolutionOutcome(
                LocationResolutionOutcomeType.NotFound, null, null));
    }
}
