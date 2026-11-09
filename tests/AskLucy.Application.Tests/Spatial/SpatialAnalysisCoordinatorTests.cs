using AskLucy.Application.Abstractions;
using AskLucy.Application.Locations;
using AskLucy.Application.Spatial;
using AskLucy.Application.Tests.SiteBoundaries;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Spatial;

/// <summary>
/// specs/094 — the coordinator's four non-negotiables: independent work runs concurrently, one
/// failure never hides another result, an empty result is not a failure, and a registered
/// specialist added outside this file is dispatched with no core edit.
/// </summary>
public sealed class SpatialAnalysisCoordinatorTests
{
    private static readonly Guid ChatId = Guid.NewGuid();

    private static readonly MapViewportContext MuscatView = new(
        CenterLatitude: 23.588,
        CenterLongitude: 58.382,
        Zoom: 12,
        SouthWestLatitude: 23.52,
        SouthWestLongitude: 58.31,
        NorthEastLatitude: 23.65,
        NorthEastLongitude: 58.45);

    private readonly SpatialLayerStateStore _store = new(TimeProvider.System);

    private SpatialAnalysisCoordinator Build(params ISpatialSpecialist[] specialists) =>
        new(
            new SpatialSpecialistRegistry(specialists),
            new SpatialBundleValidator(),
            new SpatialResultNormalizer(),
            new StudyAreaResolver(
                Substitute.For<ILocationResolutionService>(),
                Substitute.For<IUserChatRepository>(),
                TestEffectiveSiteBoundary.None()),
            _store,
            TimeProvider.System,
            NullLogger<SpatialAnalysisCoordinator>.Instance);

    private static SpatialExecutionContext Context() =>
        new("user-1", ChatId, StudyArea: null, MuscatView, []);

    private static SpatialRequestInput Input(params SpatialOperationInput[] operations) =>
        new(operations);

    private static SpatialOperationInput PoiOp(string category = "school") =>
        new(SpatialOperationKinds.PoiDiscovery, category);

    private static SpatialOperationInput ProximityOp() =>
        new(SpatialOperationKinds.ProximityAnalysis, "school", TargetCategory: "hospital", Distance: 1, Unit: "km");

    [Fact]
    public async Task ExecuteAsync_ShouldRunIndependentOperationsConcurrently()
    {
        var rendezvous = new Rendezvous();
        var coordinator = Build(
            new BarrierSpecialist(SpatialOperationKinds.PoiDiscovery, rendezvous),
            new BarrierSpecialist(SpatialOperationKinds.ProximityAnalysis, rendezvous));

        var outcome = await coordinator.ExecuteAsync(
            Input(PoiOp(), ProximityOp()), Context(), lastUserMessage: null, CancellationToken.None);

        outcome.Result.Should().NotBeNull();
        outcome.Result!.Status.Should().Be(SpatialAnalysisStatus.Complete);
        outcome.Result.Operations.Should().OnlyContain(operation => operation.Status == SpatialOperationStatus.Succeeded);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldIsolateAFailureAndKeepTheSuccessfulResult()
    {
        var coordinator = Build(
            new FixedSpecialist(SpatialOperationKinds.PoiDiscovery, _ => throw new InvalidOperationException("provider down")),
            new FixedSpecialist(SpatialOperationKinds.ProximityAnalysis, SuccessOutcome));

        var outcome = await coordinator.ExecuteAsync(
            Input(PoiOp(), ProximityOp()), Context(), lastUserMessage: null, CancellationToken.None);

        outcome.Result!.Status.Should().Be(SpatialAnalysisStatus.Partial);
        outcome.Result.Layers.Should().HaveCount(1);
        outcome.Result.Warnings.Should().ContainSingle(warning => warning.Contains("could not be completed"));
        _store.GetLayers(ChatId).Should().HaveCount(1);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReportFailedWhenEveryOperationFails()
    {
        var coordinator = Build(
            new FixedSpecialist(SpatialOperationKinds.PoiDiscovery, _ => throw new InvalidOperationException("down")));

        var outcome = await coordinator.ExecuteAsync(
            Input(PoiOp()), Context(), lastUserMessage: null, CancellationToken.None);

        outcome.Result!.Status.Should().Be(SpatialAnalysisStatus.Failed);
        outcome.Result.Layers.Should().BeEmpty();
        _store.GetLayers(ChatId).Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReportEmptyWhenNothingMatched()
    {
        var coordinator = Build(
            new FixedSpecialist(SpatialOperationKinds.PoiDiscovery, _ => SpatialSpecialistOutcome.Empty()));

        var outcome = await coordinator.ExecuteAsync(
            Input(PoiOp()), Context(), lastUserMessage: null, CancellationToken.None);

        outcome.Result!.Status.Should().Be(SpatialAnalysisStatus.Empty);
        outcome.Result.Layers.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReportAnUnregisteredKindAsNotPerformed_NotSilentlyDropIt()
    {
        var coordinator = Build(
            new FixedSpecialist(SpatialOperationKinds.PoiDiscovery, SuccessOutcome));

        var outcome = await coordinator.ExecuteAsync(
            Input(PoiOp(), new SpatialOperationInput("not.registered", "school")),
            Context(), lastUserMessage: null, CancellationToken.None);

        outcome.Result!.Status.Should().Be(SpatialAnalysisStatus.Partial);
        outcome.Result.Layers.Should().HaveCount(1);
        outcome.Result.Operations.Should().Contain(operation =>
            operation.OperationKind == "not.registered" && operation.Status == SpatialOperationStatus.Failed);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldDispatchARegisteredKindWithoutACoreEdit()
    {
        // The FR-028 proof: a new kind and its specialist, registered here, is interpreted,
        // dispatched, normalized and turned into a layer with no change to the coordinator.
        var coordinator = Build(
            new FixedSpecialist("test.echo", operation =>
                new SpatialSpecialistOutcome(
                    SpatialOperationStatus.Succeeded,
                    [new SpatialLayerDraft(
                        $"{operation.OperationId}:echo",
                        operation.OperationId,
                        "Echo",
                        "Echo",
                        SpatialLayerKind.Markers,
                        [new SpatialMarker("echo-1", "Echo", 23.5, 58.3)])],
                    [DataSourceCoverage.Complete("test", "Test")])));

        var outcome = await coordinator.ExecuteAsync(
            Input(new SpatialOperationInput("test.echo", "school")),
            Context(), lastUserMessage: null, CancellationToken.None);

        outcome.Result!.Status.Should().Be(SpatialAnalysisStatus.Complete);
        outcome.Result.Layers.Should().ContainSingle(layer => layer.Name == "Echo");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldMapRequestedBandsOntoOrderedThresholds()
    {
        // specs/093 US3 — the decider's band list is real input, not a shape the specialist alone
        // knows how to invent: it must arrive as ordered thresholds with no within/beyond mode.
        SpatialOperation? captured = null;
        var specialist = new FixedSpecialist(SpatialOperationKinds.ProximityAnalysis, operation =>
        {
            captured = operation;
            return SuccessOutcome(operation);
        });

        var outcome = await Build(specialist).ExecuteAsync(
            Input(new SpatialOperationInput(
                SpatialOperationKinds.ProximityAnalysis,
                Category: "school",
                TargetCategory: "hospital",
                Unit: "km",
                Bands: [new SpatialBandInput(0, 0.5), new SpatialBandInput(0.5, 1.5)])),
            Context(),
            lastUserMessage: null,
            CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Parameters!.DistanceThresholds!
            .Select(threshold => (threshold.LowerBound, threshold.UpperBound))
            .Should().Equal((0d, (double?)0.5), (0.5d, (double?)1.5));
        captured.Parameters.LayerMode.Should().BeNull();
        outcome.Result!.Status.Should().Be(SpatialAnalysisStatus.Complete);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectAnInvalidMapContextBeforeDispatching()
    {
        var specialist = new FixedSpecialist(SpatialOperationKinds.PoiDiscovery, SuccessOutcome);
        var coordinator = Build(specialist);
        var invalidContext = Context() with
        {
            MapContext = MuscatView with { SouthWestLatitude = 30, NorthEastLatitude = 20 },
        };

        var outcome = await coordinator.ExecuteAsync(
            Input(PoiOp()), invalidContext, lastUserMessage: null, CancellationToken.None);

        outcome.FailureReason.Should().NotBeNullOrWhiteSpace();
        outcome.Result.Should().BeNull();
        specialist.Calls.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldAskBeforeReplacingExistingLayers()
    {
        _store.ApplyLayers(
            ChatId,
            "analysis-1",
            [new SpatialLayer("schools", "analysis-1", "op-1", "Schools", "Schools", SpatialLayerKind.Markers)],
            keepExisting: false);

        var specialist = new FixedSpecialist(SpatialOperationKinds.PoiDiscovery, SuccessOutcome);
        var outcome = await Build(specialist).ExecuteAsync(
            Input(PoiOp("hospital")), Context(), lastUserMessage: null, CancellationToken.None);

        outcome.Question.Should().NotBeNullOrWhiteSpace();
        outcome.Question.Should().Contain("schools").And.Contain("hospital");
        specialist.Calls.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRetainOverlayRequestWhileAskingForAStudyArea()
    {
        var parameters = new OverlayAnalysisParameters("parcels",
            [new OverlayInput("parcels", "land-parcels", OverlayInputRole.Candidate),
             new OverlayInput("zones", "development-areas", OverlayInputRole.Constraint)],
            [new OverlayStep("eligible", OverlayOperationKind.Intersection, ["parcels", "zones"], "eligible")],
            [new OverlayCriterion("available", OverlayCriterionKind.Attribute, "parcels", "eq", "status", Text: "available")]);
        var request = new SpatialOperationInput(SpatialOperationKinds.OverlayAnalysis, OverlayParameters: parameters);
        var specialist = new FixedSpecialist(SpatialOperationKinds.OverlayAnalysis, SuccessOutcome);

        var outcome = await Build(specialist).ExecuteAsync(Input(request),
            Context() with { MapContext = null }, lastUserMessage: null, CancellationToken.None);

        outcome.PendingQuestion.Should().Be(SpatialPendingQuestion.WhichArea);
        _store.GetPending(ChatId)!.Request!.Operations[0].OverlayParameters.Should().BeEquivalentTo(parameters);
        specialist.Calls.Should().Be(0);
    }

    private static SpatialSpecialistOutcome SuccessOutcome(SpatialOperation operation) =>
        new(
            SpatialOperationStatus.Succeeded,
            [new SpatialLayerDraft(
                $"{operation.OperationId}:layer",
                operation.OperationId,
                "Layer",
                "Layer",
                SpatialLayerKind.Markers,
                [new SpatialMarker($"{operation.OperationId}-1", "Feature", 23.5, 58.3)])],
            [DataSourceCoverage.Complete("test", "Test")]);

    private sealed class FixedSpecialist(
        string operationKind,
        Func<SpatialOperation, SpatialSpecialistOutcome> respond) : ISpatialSpecialist
    {
        public int Calls { get; private set; }

        public string OperationKind => operationKind;

        public Task<SpatialSpecialistOutcome> ExecuteAsync(
            SpatialOperation operation,
            SpatialExecutionContext context,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(respond(operation));
        }
    }

    /// <summary>An async two-party rendezvous: both specialists must arrive before either proceeds.</summary>
    private sealed class Rendezvous
    {
        private readonly TaskCompletionSource _bothArrived =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _arrived;

        public async Task<bool> MeetAsync()
        {
            if (Interlocked.Increment(ref _arrived) == 2)
            {
                _bothArrived.TrySetResult();
            }

            try
            {
                await _bothArrived.Task.WaitAsync(TimeSpan.FromSeconds(5));
                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
        }
    }

    private sealed class BarrierSpecialist(string operationKind, Rendezvous rendezvous) : ISpatialSpecialist
    {
        public string OperationKind => operationKind;

        public async Task<SpatialSpecialistOutcome> ExecuteAsync(
            SpatialOperation operation,
            SpatialExecutionContext context,
            CancellationToken cancellationToken)
        {
            // Both specialists must reach the rendezvous together. If dispatch were sequential,
            // the first would time out — the test fails rather than merely running slowly.
            var enteredTogether = await rendezvous.MeetAsync();
            return enteredTogether
                ? SuccessOutcome(operation)
                : SpatialSpecialistOutcome.Failed("the operations did not run concurrently");
        }
    }
}
