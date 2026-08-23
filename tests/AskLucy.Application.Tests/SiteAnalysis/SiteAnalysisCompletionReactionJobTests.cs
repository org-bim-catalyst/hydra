using AskLucy.Application.Abstractions;
using AskLucy.Application.Panels;
using AskLucy.Application.SiteAnalysis;
using AskLucy.Application.SiteAnalysis.AgentExecutionCompletion;
using AskLucy.Application.SiteAnalysis.Commands.VerifyAndLinkDigitalCoreProject;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Chats;
using AskLucy.Domain.SiteAnalysis;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.SiteAnalysis;

public sealed class SiteAnalysisCompletionReactionJobTests
{
    private readonly IAgentExecutionRepository _executionRepository = Substitute.For<IAgentExecutionRepository>();
    private readonly ISiteAnalysisProjectLinkRepository _linkRepository = Substitute.For<ISiteAnalysisProjectLinkRepository>();
    private readonly IUserChatRepository _userChatRepository = Substitute.For<IUserChatRepository>();
    private readonly IMessageRepository _messageRepository = Substitute.For<IMessageRepository>();
    private readonly IPanelNotifier _panelNotifier = Substitute.For<IPanelNotifier>();
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private static readonly Guid AgentId = Guid.NewGuid();

    private SiteAnalysisCompletionReactionJob CreateJob() => new(
        _executionRepository, _linkRepository, _userChatRepository, _messageRepository, _panelNotifier, _sender, _unitOfWork);

    private (AgentExecution Execution, Guid ChatId) CreateCompletedBoundaryExecution(string outputJson)
    {
        var chatId = Guid.NewGuid();
        var execution = AgentExecution.Create(
            AgentId, Guid.NewGuid(), "user-1", "Resolve the site boundary for Al Safa Park", false,
            AgentConversationIntegrationMode.ExistingConversation, chatId, "user-1");
        var step = execution.AddStep(0, "Resolve boundary", AgentExecutionStepType.ToolCall, null, SiteAnalysisToolNames.ResolveSiteBoundary, null);
        step.Start();
        step.Complete(outputJson);
        execution.Start();
        execution.Complete(null, """{"citations":[]}""");

        _executionRepository.GetByIdAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(execution);
        _userChatRepository.GetByIdAsync(chatId, Arg.Any<CancellationToken>())
            .Returns(UserChat.Create("Al Safa Park session", "user-1", null, "user-1"));

        return (execution, chatId);
    }

    [Fact]
    public async Task ProcessAsync_ShouldAskForClarification_WithoutDispatchingAPanelOrSearching_WhenCandidatesAreAmbiguous()
    {
        var (execution, chatId) = CreateCompletedBoundaryExecution(
            """{"resolved":true,"builtAssetConfirmed":true,"resolvedName":"Springfield Park","latitude":25.1,"longitude":55.2,"candidateCount":3}""");
        _linkRepository.GetByUserChatIdAsync(chatId, Arg.Any<CancellationToken>()).Returns((SiteAnalysisProjectLink?)null);

        await CreateJob().ProcessAsync(execution.Id, CancellationToken.None);

        await _panelNotifier.DidNotReceiveWithAnyArgs().PanelRequestedAsync(default!, default!, default);
        await _sender.DidNotReceiveWithAnyArgs().Send(default(object)!, Arg.Any<CancellationToken>());
        _messageRepository.Received(1).Add(Arg.Is<Message>(m => m.Content.Contains("more than one place")));
    }

    [Fact]
    public async Task ProcessAsync_ShouldDispatchBoundaryPanelAndSkipTheDigitalCoreSearch_WhenAlreadyLinked()
    {
        var (execution, chatId) = CreateCompletedBoundaryExecution(
            """{"resolved":true,"builtAssetConfirmed":true,"resolvedName":"Al Safa Park","latitude":25.15,"longitude":55.22,"candidateCount":1}""");
        var existingLink = SiteAnalysisProjectLink.Create(chatId, "tdc-1", SiteAnalysisProjectLinkSource.BootstrapCreated, "Al Safa Park", null, null);
        _linkRepository.GetByUserChatIdAsync(chatId, Arg.Any<CancellationToken>()).Returns(existingLink);

        await CreateJob().ProcessAsync(execution.Id, CancellationToken.None);

        await _panelNotifier.Received(1).PanelRequestedAsync(
            "user-1", Arg.Is<PanelRequestDto>(p => p.TypeKey == "site-analysis-boundary"), Arg.Any<CancellationToken>());
        await _sender.DidNotReceiveWithAnyArgs().Send(default(object)!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_ShouldDispatchBoundaryPanelAndRunBootstrapSearch_WhenNotYetLinked()
    {
        var (execution, chatId) = CreateCompletedBoundaryExecution(
            """{"resolved":true,"builtAssetConfirmed":true,"resolvedName":"Al Safa Park","latitude":25.15,"longitude":55.22,"candidateCount":1}""");
        _linkRepository.GetByUserChatIdAsync(chatId, Arg.Any<CancellationToken>()).Returns((SiteAnalysisProjectLink?)null);
        _sender.Send(Arg.Any<VerifyAndLinkDigitalCoreProjectCommand>(), Arg.Any<CancellationToken>())
            .Returns(new VerifyAndLinkDigitalCoreProjectResult(VerifyAndLinkDigitalCoreProjectOutcome.AwaitingCreateConfirmation, null, null));

        await CreateJob().ProcessAsync(execution.Id, CancellationToken.None);

        await _panelNotifier.Received(1).PanelRequestedAsync(
            "user-1", Arg.Is<PanelRequestDto>(p => p.TypeKey == "site-analysis-boundary"), Arg.Any<CancellationToken>());
        await _sender.Received(1).Send(Arg.Any<VerifyAndLinkDigitalCoreProjectCommand>(), Arg.Any<CancellationToken>());
    }
}
