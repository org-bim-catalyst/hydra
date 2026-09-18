using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Panels;
using AskLucy.Application.SiteAnalysis;
using AskLucy.Domain.SiteAnalysis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.SiteAnalysis;

/// <summary>
/// specs/057-site-analysis-agent tasks.md T056-T059 — proves the delivery mechanism the deferred
/// specialists will rely on (tasks.md rules 1 and 5) through a REAL
/// <see cref="SiteAnalysisResultRelay"/>, driven the same way
/// <c>WorkflowExecutionOrchestrator.ExecuteParallelAsync</c> drives concurrent branches
/// (<see cref="Task.WhenAll"/>), not a hand-rolled stand-in for the relay itself.
///
/// <para>
/// This release ships exactly one <see cref="SiteAnalysisType"/>, so a literal two-specialist fan
/// out within one analysis cannot be constructed without adding a second production enum member —
/// the temporary, then-reverted addition tasks.md T056/T060 describe for a manual,
/// running-application walkthrough (quickstart.md Scenario 8/9), which is out of reach in this
/// environment (no live app, no browser). Reproduced here instead as two <b>independent</b> site
/// analyses reporting concurrently: the property under test — does a branch's relay call arrive
/// the moment that branch finishes, never batched with a sibling; does one branch's failure leave
/// an unrelated concurrent branch's success untouched — depends only on the relay's own mechanics,
/// not on which two things happen to be running side by side. Nothing here touches production
/// code; there is no throwaway specialist left behind to remove.
/// </para>
/// </summary>
public sealed class SiteAnalysisProgressiveDeliveryTests
{
    private const string UserId = "user-1";
    private static readonly Guid UserChatId = Guid.NewGuid();

    private readonly ISiteAnalysisRepository _repository = Substitute.For<ISiteAnalysisRepository>();
    private readonly IMessageRepository _messageRepository = Substitute.For<IMessageRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ISiteAnalysisNotifier _notifier = Substitute.For<ISiteAnalysisNotifier>();
    private readonly IPanelNotifier _panelNotifier = Substitute.For<IPanelNotifier>();

    private ISiteAnalysisResultRelay BuildRelay() =>
        new SiteAnalysisResultRelay(_repository, _messageRepository, _unitOfWork, _notifier, _panelNotifier, new SiteAnalysisContentComposer(), NullLogger<SiteAnalysisResultRelay>.Instance);

    /// <summary>Stands in for a specialist tool: waits, then reports through the REAL relay — exactly what <c>SiteSchematicImageGenerationTool</c> does, minus the AI call.</summary>
    private static async Task RunFakeSpecialistAsync(
        Guid siteAnalysisId, TimeSpan delay, ISiteAnalysisResultRelay relay, string name, List<string> deliveryOrder, bool shouldFail = false)
    {
        await Task.Delay(delay);

        if (shouldFail)
        {
            await relay.ReportFailureAsync(siteAnalysisId, SiteAnalysisType.SchematicImage, "simulated failure", cause: null);
            lock (deliveryOrder) { deliveryOrder.Add(name); }
            return;
        }

        var metadata = new SiteAnalysisResultMetadata(SiteAnalysisType.SchematicImage, "Test Site", "test-source", SiteAnalysisConfidenceLevel.Medium, DateTime.UtcNow, []);
        using var content = new SiteAnalysisContentComposer().ComposeFinding("Test Finding", "body", image: null, metadata);

        // SchematicImage is a document-bearing analysis type (SiteAnalysisResultRelay's
        // AnalysisTypesRequiringDocument), so the validation gate requires a non-null documentId.
        await relay.ReportSuccessAsync(siteAnalysisId, SiteAnalysisType.SchematicImage, metadata, content, documentId: Guid.NewGuid());
        lock (deliveryOrder) { deliveryOrder.Add(name); }
    }

    private static Domain.SiteAnalysis.SiteAnalysis CreateAnalysis() =>
        Domain.SiteAnalysis.SiteAnalysis.Create(UserId, UserChatId, "Test Site", 25.09, 55.20, null, expectedResultCount: 1, UserId);

    [Fact]
    public async Task Relay_ShouldDeliverEachFinding_TheMomentItFinishes_NotBatchedWithASibling()
    {
        // tasks.md rule 1 / T057 — SiteAnalysisResultRelayTests already proves one report's
        // notification fires inline; this proves two concurrent reports interleave by completion
        // order (fast before slow) rather than the fast one waiting for the slow one.
        var fastAnalysis = CreateAnalysis();
        var slowAnalysis = CreateAnalysis();
        _repository.GetByIdAsync(fastAnalysis.Id, Arg.Any<CancellationToken>()).Returns(fastAnalysis);
        _repository.GetByIdAsync(slowAnalysis.Id, Arg.Any<CancellationToken>()).Returns(slowAnalysis);

        var deliveryOrder = new List<string>();
        var relay = BuildRelay();

        await Task.WhenAll(
            RunFakeSpecialistAsync(fastAnalysis.Id, TimeSpan.FromMilliseconds(10), relay, "fast", deliveryOrder),
            RunFakeSpecialistAsync(slowAnalysis.Id, TimeSpan.FromMilliseconds(200), relay, "slow", deliveryOrder));

        deliveryOrder.Should().Equal("fast", "slow");
        fastAnalysis.Status.Should().Be(SiteAnalysisStatus.Completed);
        slowAnalysis.Status.Should().Be(SiteAnalysisStatus.Completed);
    }

    [Fact]
    public async Task Relay_ShouldStillDeliverTheSurvivingFinding_WhenAConcurrentSiblingAnalysisFails()
    {
        // tasks.md rule 5 / T059 — a failing report must never affect an unrelated concurrent
        // one; SiteAnalysisResultRelayTests proves the tolerance sequentially, this proves it
        // holds when a failing and a succeeding report race concurrently.
        var okAnalysis = CreateAnalysis();
        var failAnalysis = CreateAnalysis();
        _repository.GetByIdAsync(okAnalysis.Id, Arg.Any<CancellationToken>()).Returns(okAnalysis);
        _repository.GetByIdAsync(failAnalysis.Id, Arg.Any<CancellationToken>()).Returns(failAnalysis);

        var deliveryOrder = new List<string>();
        var relay = BuildRelay();

        await Task.WhenAll(
            RunFakeSpecialistAsync(okAnalysis.Id, TimeSpan.FromMilliseconds(10), relay, "ok", deliveryOrder),
            RunFakeSpecialistAsync(failAnalysis.Id, TimeSpan.FromMilliseconds(20), relay, "fails", deliveryOrder, shouldFail: true));

        okAnalysis.Status.Should().Be(SiteAnalysisStatus.Completed);
        failAnalysis.Status.Should().Be(SiteAnalysisStatus.Failed);
        await _notifier.Received(1).ResultReceivedAsync(UserId, Arg.Any<SiteAnalysisResultReceivedDto>(), Arg.Any<CancellationToken>());
        await _panelNotifier.Received(1).PanelRequestedAsync(UserId, Arg.Any<PanelRequestDto>(), Arg.Any<CancellationToken>());
    }
}
