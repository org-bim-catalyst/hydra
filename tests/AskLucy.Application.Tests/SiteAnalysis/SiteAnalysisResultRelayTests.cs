using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Panels;
using AskLucy.Application.SiteAnalysis;
using AskLucy.Domain.Chats;
using AskLucy.Domain.SiteAnalysis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.SiteAnalysis;

/// <summary>
/// contracts/result-relay.md — the parent's validation gate, delivery ordering, and closing
/// outcome. specs/057-site-analysis-agent tasks.md T037.
/// </summary>
public sealed class SiteAnalysisResultRelayTests
{
    private const string UserId = "user-1";
    private static readonly Guid UserChatId = Guid.NewGuid();

    private readonly ISiteAnalysisRepository _repository = Substitute.For<ISiteAnalysisRepository>();
    private readonly IMessageRepository _messageRepository = Substitute.For<IMessageRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ISiteAnalysisNotifier _notifier = Substitute.For<ISiteAnalysisNotifier>();
    private readonly IPanelNotifier _panelNotifier = Substitute.For<IPanelNotifier>();

    private SiteAnalysisResultRelay BuildRelay() =>
        new(_repository, _messageRepository, _unitOfWork, _notifier, _panelNotifier, NullLogger<SiteAnalysisResultRelay>.Instance);

    private static Domain.SiteAnalysis.SiteAnalysis CreateAnalysis(int expectedResultCount = 1) =>
        Domain.SiteAnalysis.SiteAnalysis.Create(UserId, UserChatId, "Al Barsha South", 25.09, 55.20, null, expectedResultCount, UserId);

    private static SiteAnalysisResultMetadata ValidMetadata() =>
        new(SiteAnalysisType.SchematicImage, "Al Barsha South", "openai:gpt-image-1", SiteAnalysisConfidenceLevel.Medium, DateTime.UtcNow, []);

    private static JsonDocument ValidContent() =>
        JsonDocument.Parse("""{"version":1,"blocks":[{"kind":"heading","text":"Schematic Site Map"}]}""");

    [Fact]
    public async Task ReportSuccessAsync_ShouldPersistCompletedResult_ThenPersistMessage_ThenNotify_ThenPushPanel()
    {
        var analysis = CreateAnalysis();
        _repository.GetByIdAsync(analysis.Id, Arg.Any<CancellationToken>()).Returns(analysis);

        var relay = BuildRelay();
        await relay.ReportSuccessAsync(analysis.Id, SiteAnalysisType.SchematicImage, ValidMetadata(), ValidContent(), Guid.NewGuid(), TestContext.Current.CancellationToken);

        analysis.Results.Should().ContainSingle(r => r.Status == SiteAnalysisResultStatus.Completed);
        _messageRepository.Received(1).Add(Arg.Is<Message>(m => m!.Role == MessageRole.Assistant && m.UserChatId == UserChatId));
        await _notifier.Received(1).ResultReceivedAsync(UserId, Arg.Any<SiteAnalysisResultReceivedDto>(), Arg.Any<CancellationToken>());
        await _panelNotifier.Received(1).PanelRequestedAsync(UserId, Arg.Any<PanelRequestDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReportSuccessAsync_ShouldReject_WhenDataSourceIsMissing()
    {
        var analysis = CreateAnalysis();
        _repository.GetByIdAsync(analysis.Id, Arg.Any<CancellationToken>()).Returns(analysis);
        var metadata = ValidMetadata() with { DataSource = "" };

        var relay = BuildRelay();
        await relay.ReportSuccessAsync(analysis.Id, SiteAnalysisType.SchematicImage, metadata, ValidContent(), Guid.NewGuid(), TestContext.Current.CancellationToken);

        analysis.Results.Should().ContainSingle(r => r.Status == SiteAnalysisResultStatus.Rejected);
        await _notifier.DidNotReceive().ResultReceivedAsync(Arg.Any<string>(), Arg.Any<SiteAnalysisResultReceivedDto>(), Arg.Any<CancellationToken>());
        await _panelNotifier.DidNotReceive().PanelRequestedAsync(Arg.Any<string>(), Arg.Any<PanelRequestDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReportSuccessAsync_ShouldReject_WhenTheDeclaredFileOutputHasNoDocumentId()
    {
        var analysis = CreateAnalysis();
        _repository.GetByIdAsync(analysis.Id, Arg.Any<CancellationToken>()).Returns(analysis);

        var relay = BuildRelay();
        await relay.ReportSuccessAsync(analysis.Id, SiteAnalysisType.SchematicImage, ValidMetadata(), ValidContent(), documentId: null, TestContext.Current.CancellationToken);

        analysis.Results.Should().ContainSingle(r => r.Status == SiteAnalysisResultStatus.Rejected);
    }

    [Fact]
    public async Task ReportFailureAsync_ShouldPersistFailure_AndNotifyNobody()
    {
        var analysis = CreateAnalysis();
        _repository.GetByIdAsync(analysis.Id, Arg.Any<CancellationToken>()).Returns(analysis);

        var relay = BuildRelay();
        await relay.ReportFailureAsync(analysis.Id, SiteAnalysisType.SchematicImage, "provider unavailable", cause: null, TestContext.Current.CancellationToken);

        analysis.Results.Should().ContainSingle(r => r.Status == SiteAnalysisResultStatus.Failed && r.FailureReason == "provider unavailable");
        await _notifier.DidNotReceive().ResultReceivedAsync(Arg.Any<string>(), Arg.Any<SiteAnalysisResultReceivedDto>(), Arg.Any<CancellationToken>());
        await _panelNotifier.DidNotReceiveWithAnyArgs().PanelRequestedAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReportFailureAsync_ShouldStillDeliverTheClosingOutcome_WhenItIsTheLastOutstandingSpecialist()
    {
        var analysis = CreateAnalysis(expectedResultCount: 1);
        _repository.GetByIdAsync(analysis.Id, Arg.Any<CancellationToken>()).Returns(analysis);

        var relay = BuildRelay();
        await relay.ReportFailureAsync(analysis.Id, SiteAnalysisType.SchematicImage, "provider unavailable", cause: null, TestContext.Current.CancellationToken);

        analysis.Status.Should().Be(SiteAnalysisStatus.Failed);
        await _notifier.Received(1).AnalysisCompletedAsync(
            UserId, Arg.Is<SiteAnalysisCompletedDto>(dto => dto!.Status == "Failed" && dto.NoticeText != null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClosingOutcome_ShouldNotFireASecondTime_WhenTheClaimIsAlreadyTaken()
    {
        // T055's "exactly once under two simultaneous final reports" is proven at the domain
        // level by SiteAnalysisTests.TryClaimClosingOutcome_ShouldReturnTrue_OnlyOnce — the
        // primitive the relay's settlement logic relies on. A relay-level reproduction with two
        // *distinct* specialists reporting concurrently is not constructible in this release: this
        // spec ships exactly one SiteAnalysisType (SchematicImage), and the aggregate's own
        // EnsureNotAlreadySettled invariant forbids the same type reporting twice — the very
        // thing that would need faking to simulate a second specialist. This test instead proves
        // the relay-level consequence directly: once TryClaimClosingOutcome has been taken (by
        // the single specialist's own settlement), calling it again against the same instance
        // yields no further notification — the exact guard a second, real specialist would hit.
        var analysis = CreateAnalysis(expectedResultCount: 1);
        _repository.GetByIdAsync(analysis.Id, Arg.Any<CancellationToken>()).Returns(analysis);
        analysis.TryClaimClosingOutcome("pre-claimed-by-a-simulated-sibling").Should().BeTrue();

        var relay = BuildRelay();
        await relay.ReportSuccessAsync(analysis.Id, SiteAnalysisType.SchematicImage, ValidMetadata(), ValidContent(), Guid.NewGuid(), TestContext.Current.CancellationToken);

        await _notifier.DidNotReceiveWithAnyArgs().AnalysisCompletedAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReportSuccessAsync_ShouldEmitNoClosingNotice_WhenEverySpecialistSucceeded()
    {
        var analysis = CreateAnalysis(expectedResultCount: 1);
        _repository.GetByIdAsync(analysis.Id, Arg.Any<CancellationToken>()).Returns(analysis);

        var relay = BuildRelay();
        await relay.ReportSuccessAsync(analysis.Id, SiteAnalysisType.SchematicImage, ValidMetadata(), ValidContent(), Guid.NewGuid(), TestContext.Current.CancellationToken);

        analysis.Status.Should().Be(SiteAnalysisStatus.Completed);
        await _notifier.Received(1).AnalysisCompletedAsync(
            UserId, Arg.Is<SiteAnalysisCompletedDto>(dto => dto!.NoticeText == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReportSuccessAsync_ShouldReject_AndNotCountTowardSucceeded_WhenContentHasNoBlocks()
    {
        // T044 — extends the DataSource-missing case above (contracts/result-relay.md §
        // Validation gate "Provenance present"): ConfidenceLevel itself cannot independently be
        // "missing" since SiteAnalysisResultMetadata's ConfidenceLevel is a non-nullable enum: a
        // structurally malformed report is instead exercised here via empty content, and the test
        // additionally asserts a Rejected result does not count toward the closing outcome's
        // succeededCount — the nuance FR-025-FR-027's "how many completed" wording depends on.
        var analysis = CreateAnalysis(expectedResultCount: 1);
        _repository.GetByIdAsync(analysis.Id, Arg.Any<CancellationToken>()).Returns(analysis);
        using var emptyContent = JsonDocument.Parse("""{"version":1,"blocks":[]}""");

        var relay = BuildRelay();
        await relay.ReportSuccessAsync(analysis.Id, SiteAnalysisType.SchematicImage, ValidMetadata(), emptyContent, Guid.NewGuid(), TestContext.Current.CancellationToken);

        analysis.Results.Should().ContainSingle(r => r.Status == SiteAnalysisResultStatus.Rejected);
        await _notifier.Received(1).AnalysisCompletedAsync(
            UserId, Arg.Is<SiteAnalysisCompletedDto>(dto => dto!.SucceededCount == 0 && dto.Status == "Failed"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReportSuccessAsync_ShouldDoNothing_WhenTheAnalysisNoLongerExists()
    {
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Domain.SiteAnalysis.SiteAnalysis?)null);

        var relay = BuildRelay();
        await relay.ReportSuccessAsync(Guid.NewGuid(), SiteAnalysisType.SchematicImage, ValidMetadata(), ValidContent(), Guid.NewGuid(), TestContext.Current.CancellationToken);

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
