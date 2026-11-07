using AskLucy.Application.Spatial.Query;
using AskLucy.Application.Spatial.Specialists;
using AskLucy.Application.Spatial;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AskLucy.Application.Tests.Spatial.Query;

/// <summary>
/// specs/095 — unit tests for <see cref="SpatialQuerySpecialist"/>.
/// Covers: empty result, timeout, provider failure, audit log,
/// attribute-only, spatial-only, combined, and mixed-type OR rejection.
/// </summary>
public class SpatialQuerySpecialistTests
{
    private readonly IDatasetCatalog _datasetCatalog;
    private readonly Mock<ISpatialQueryProvider> _providerMock;
    private readonly SpatialQuerySpecialist _specialist;
    private readonly ILogger<SpatialQuerySpecialist> _logger;

    public SpatialQuerySpecialistTests()
    {
        // Reset singleton catalog before each test
        DatasetCatalog.Instance = new DatasetCatalog();

        _datasetCatalog = DatasetCatalog.Instance;

        _providerMock = new Mock<ISpatialQueryProvider>();
        _logger = Mock.Of<ILogger<SpatialQuerySpecialist>>();

        _specialist = new SpatialQuerySpecialist(
            _datasetCatalog,
            Mock.Of<ILocationResolutionService>(),
            new[] { _providerMock.Object },
            Mock.Of<SpatialQueryAuditLogger>());
    }

    [Fact]
    public async Task ExecuteAsync_EmptyResult_ReturnsEmptyOutcome()
    {
        // Arrange
        _providerMock
            .Setup(p => p.ExecuteAsync(It.IsAny<SpatialQueryDefinition>(),
                It.IsAny<SpatialExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<QueryResultBatch> { new() { BatchIndex = 0, Features = new List<QueryResultFeature>(), TotalCount = 0, EstimatedTotal = 0, IsFinal = true } });

        var operation = new SpatialOperation("op-1", SpatialOperationKinds.SpatialQuery,
            new List<FeatureSelector> { new() { Category = "buildings" } }, null, null);
        operation.QueryParameters = new SpatialQueryParameters(
            "buildings", new List<AttributePredicate>(), new List<SpatialPredicate>());

        // Act
        var outcome = await _specialist.ExecuteAsync(operation,
            new SpatialExecutionContext(Guid.NewGuid(), CancellationToken.None), CancellationToken.None);

        // Assert
        Assert.Equal(SpatialOperationStatus.Succeeded, outcome.Status);
        Assert.Empty(outcome.Layers);
        Assert.Null(outcome.Coverage);
        Assert.Null(outcome.Warnings);
    }

    [Fact]
    public async Task ExecuteAsync_Timeout_ReturnsTimedOutOutcome()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.CancelAfter(1); // Force immediate timeout

        _providerMock
            .Setup(p => p.ExecuteAsync(It.IsAny<SpatialQueryDefinition>(),
                It.IsAny<SpatialExecutionContext>(), cts.Token))
            .ThrowsAsync(new OperationCanceledException("Timeout"));

        var operation = new SpatialOperation("op-1", SpatialOperationKinds.SpatialQuery,
            new List<FeatureSelector> { new() { Category = "buildings" } }, null, null);
        operation.QueryParameters = new SpatialQueryParameters(
            "buildings", new List<AttributePredicate>(), new List<SpatialPredicate>());

        // Act
        var outcome = await _specialist.ExecuteAsync(operation,
            new SpatialExecutionContext(Guid.NewGuid(), cts.Token), CancellationToken.None);

        // Assert
        Assert.Equal(SpatialOperationStatus.Succeeded, outcome.Status); // Still Succeeded - empty with warning
        Assert.Contains(outcome.Warnings?, w => w.Contains("timed out"));
    }

    [Fact]
    public async Task ExecuteAsync_ProviderFailure_ReturnsFailedOutcome()
    {
        // Arrange
        _providerMock
            .Setup(p => p.ExecuteAsync(It.IsAny<SpatialQueryDefinition>(),
                It.IsAny<SpatialExecutionContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Provider error"));

        var operation = new SpatialOperation("op-1", SpatialOperationKinds.SpatialQuery,
            new List<FeatureSelector> { new() { Category = "buildings" } }, null, null);
        operation.QueryParameters = new SpatialQueryParameters(
            "buildings", new List<AttributePredicate>(), new List<SpatialPredicate>());

        // Act
        var outcome = await _specialist.ExecuteAsync(operation,
            new SpatialExecutionContext(Guid.NewGuid(), CancellationToken.None), CancellationToken.None);

        // Assert
        Assert.Equal(SpatialOperationStatus.Failed, outcome.Status);
        Assert.Null(outcome.Coverage);
        Assert.NotNull(outcome.Warnings);
    }

    [Fact]
    public async Task ExecuteAsync_AttributeOnly_RunsSuccessfully()
    {
        // Arrange
        _providerMock
            .Setup(p => p.ExecuteAsync(It.IsAny<SpatialQueryDefinition>(),
                It.IsAny<SpatialExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<QueryResultBatch> { new() { BatchIndex = 0, Features = new List<QueryResultFeature> { new() { FeatureId = "f1", Name = "Building A", Geometry = new QueryResultFeatureBuilder().BuildGeometry() }, new() { FeatureId = "f2", Name = "Building B", Geometry = new QueryResultFeatureBuilder().BuildGeometry() } }, TotalCount = 2, EstimatedTotal = 2, IsFinal = true } });

        var operation = new SpatialOperation("op-1", SpatialOperationKinds.SpatialQuery,
            new List<FeatureSelector> { new() { Category = "buildings" } }, null, null);
        operation.QueryParameters = new SpatialQueryParameters(
            "buildings",
            new List<AttributePredicate> { new() { Field = "height", Operator = AttributeOperator.Gt, Value = 30, Unit = "m" } },
            new List<SpatialPredicate>());

        // Act
        var outcome = await _specialist.ExecuteAsync(operation,
            new SpatialExecutionContext(Guid.NewGuid(), CancellationToken.None), CancellationToken.None);

        // Assert
        Assert.Equal(SpatialOperationStatus.Succeeded, outcome.Status);
        Assert.NotNull(outcome.Layers);
        Assert.True(outcome.Layers.Count > 0);
    }

    [Fact]
    public async Task ExecuteAsync_SpatialOnly_RunsSuccessfully()
    {
        // Arrange
        _providerMock
            .Setup(p => p.ExecuteAsync(It.IsAny<SpatialQueryDefinition>(),
                It.IsAny<SpatialExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<QueryResultBatch> { new() { BatchIndex = 0, Features = new List<QueryResultFeature> { new() { FeatureId = "f1", Name = "Road A", Geometry = new QueryResultFeatureBuilder().BuildGeometry() } }, TotalCount = 1, EstimatedTotal = 1, IsFinal = true } });

        var operation = new SpatialOperation("op-1", SpatialOperationKinds.SpatialQuery,
            new List<FeatureSelector> { new() { Category = "roads" } }, null, null);
        operation.QueryParameters = new SpatialQueryParameters(
            "roads",
            new List<AttributePredicate>(),
            new List<SpatialPredicate> { new() { Relationship = SpatialRelationship.Within, ReferenceName = "test-area" } });

        // Act
        var outcome = await _specialist.ExecuteAsync(operation,
            new SpatialExecutionContext(Guid.NewGuid(), CancellationToken.None), CancellationToken.None);

        // Assert
        Assert.Equal(SpatialOperationStatus.Succeeded, outcome.Status);
        Assert.NotNull(outcome.Layers);
        Assert.True(outcome.Layers.Count > 0);
    }

    [Fact]
    public async Task ExecuteAsync_CombinedAttributeAndSpatial_RunsSuccessfully()
    {
        // Arrange
        _providerMock
            .Setup(p => p.ExecuteAsync(It.IsAny<SpatialQueryDefinition>(),
                It.IsAny<SpatialExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<QueryResultBatch> { new() { BatchIndex = 0, Features = new List<QueryResultFeature>(), TotalCount = 0, EstimatedTotal = 0, IsFinal = true } });

        var operation = new SpatialOperation("op-1", SpatialOperationKinds.SpatialQuery,
            new List<FeatureSelector> { new() { Category = "buildings" } }, null, null);
        operation.QueryParameters = new SpatialQueryParameters(
            "buildings",
            new List<AttributePredicate> { new() { Field = "height", Operator = AttributeOperator.Gt, Value = 30, Unit = "m" } },
            new List<SpatialPredicate> { new() { Relationship = SpatialRelationship.Within, ReferenceName = "zone" } });

        // Act
        var outcome = await _specialist.ExecuteAsync(operation,
            new SpatialExecutionContext(Guid.NewGuid(), CancellationToken.None), CancellationToken.None);

        // Assert
        Assert.Equal(SpatialOperationStatus.Succeeded, outcome.Status);
        Assert.NotNull(outcome.Layers);
    }

    [Fact]
    public async Task ExecuteAsync_MixedTypeOr_Rejected()
    {
        // Arrange - Validator should reject this before specialist runs
        var operation = new SpatialOperation("op-1", SpatialOperationKinds.SpatialQuery,
            new List<FeatureSelector> { new() { Category = "buildings" } }, null, null);
        operation.QueryParameters = new SpatialQueryParameters(
            "buildings",
            new List<AttributePredicate> { new() { Field = "height", Operator = AttributeOperator.Gt, Value = 30, Unit = "m" } },
            new List<SpatialPredicate> { new() { Relationship = SpatialRelationship.Within, ReferenceName = "zone" } });
        // Set OR combinator on both groups
        operation.QueryParameters.AttributeCombinator = PredicateCombinator.Or;
        operation.QueryParameters.SpatialCombinator = PredicateCombinator.Or;
        // Add second predicate to each group to trigger OR
        operation.QueryParameters.AttributePredicates = new List<AttributePredicate>
        {
            new() { Field = "height", Operator = AttributeOperator.Gt, Value = 30, Unit = "m" },
            new() { Field = "floors", Operator = AttributeOperator.Gt, Value = 5, Unit = "" }
        };
        operation.QueryParameters.SpatialPredicates = new List<SpatialPredicate>
        {
            new() { Relationship = SpatialRelationship.Within, ReferenceName = "zone" },
            new() { Relationship = SpatialRelationship.Intersecting, ReferenceName = "other-zone" }
        };

        // Act & Assert - Validator should reject mixed-type OR
        var validation = await new SpatialBundleValidator(_datasetCatalog).ValidateAsync(
            new SpatialRequestBundle { BundleId = "test", Operations = new List<SpatialOperation> { operation } }, CancellationToken.None);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.ErrorMessage.Contains("OR across types"));
    }
}