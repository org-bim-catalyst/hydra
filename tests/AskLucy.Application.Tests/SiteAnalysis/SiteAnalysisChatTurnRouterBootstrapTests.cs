using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents;
using AskLucy.Application.Agents.Commands.StartAgentExecution;
using AskLucy.Application.Options;
using AskLucy.Application.SiteAnalysis;
using AskLucy.Application.SiteAnalysis.Commands.VerifyAndLinkDigitalCoreProject;
using AskLucy.Application.SiteAnalysis.Routing;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Chats;
using AskLucy.Domain.SiteAnalysis;
using FluentAssertions;
using Hangfire;
using MediatR;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.SiteAnalysis;

public sealed class SiteAnalysisChatTurnRouterBootstrapTests
{
    private readonly ISiteAnalysisProjectLinkRepository _linkRepository = Substitute.For<ISiteAnalysisProjectLinkRepository>();
    private readonly IAgentExecutionRepository _executionRepository = Substitute.For<IAgentExecutionRepository>();
    private readonly IMessageRepository _messageRepository = Substitute.For<IMessageRepository>();
    private readonly IUserChatRepository _userChatRepository = Substitute.For<IUserChatRepository>();
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly IBackgroundJobClient _backgroundJobClient = Substitute.For<IBackgroundJobClient>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private static readonly Guid ConfiguredAgentId = Guid.NewGuid();

    private SiteAnalysisConversationStateAssembler CreateStateAssembler() => new(_executionRepository);

    private SiteAnalysisChatTurnRouter CreateRouter() => new(
        _linkRepository, CreateStateAssembler(), _messageRepository, _userChatRepository, _sender,
        Microsoft.Extensions.Options.Options.Create(new SiteAnalysisOptions { AgentId = ConfiguredAgentId }), _backgroundJobClient, _unitOfWork);

    [Fact]
    public async Task HandleUserMessage_ShouldNoOp_WhenAgentIdIsNotConfigured()
    {
        var router = new SiteAnalysisChatTurnRouter(
            _linkRepository, CreateStateAssembler(), _messageRepository, _userChatRepository, _sender,
            Microsoft.Extensions.Options.Options.Create(new SiteAnalysisOptions()), _backgroundJobClient, _unitOfWork);

        await router.HandleUserMessageAsync(Guid.NewGuid(), "I want to redesign Al Safa Park in Dubai.", CancellationToken.None);

        await _sender.DidNotReceiveWithAnyArgs().Send(default(object)!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleUserMessage_ShouldNoOp_WhenAlreadyLinkedAndBoundaryAlreadyResolved()
    {
        var chatId = Guid.NewGuid();
        var existingLink = SiteAnalysisProjectLink.Create(chatId, "tdc-1", SiteAnalysisProjectLinkSource.BootstrapCreated, "Al Safa Park", null, null);
        _linkRepository.GetByUserChatIdAsync(chatId, Arg.Any<CancellationToken>()).Returns(existingLink);

        var execution = AgentExecution.Create(
            ConfiguredAgentId, Guid.NewGuid(), "user-1", "Resolve the site boundary for Al Safa Park", false,
            AgentConversationIntegrationMode.ExistingConversation, chatId, "user-1");
        var step = execution.AddStep(0, "Resolve boundary", AgentExecutionStepType.ToolCall, null, SiteAnalysisToolNames.ResolveSiteBoundary, null);
        step.Start();
        step.Complete("""{"resolved":true,"builtAssetConfirmed":true,"resolvedName":"Al Safa Park","latitude":25.15,"longitude":55.22,"candidateCount":1}""");
        _executionRepository.ListCompletedStepsByUserChatIdAsync(chatId, Arg.Any<CancellationToken>()).Returns([step]);

        await CreateRouter().HandleUserMessageAsync(chatId, "how good is this for recreation?", CancellationToken.None);

        await _sender.DidNotReceiveWithAnyArgs().Send(default(object)!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleUserMessage_ShouldStartAnAgentExecution_ForANewSiteDescription()
    {
        var chatId = Guid.NewGuid();
        _linkRepository.GetByUserChatIdAsync(chatId, Arg.Any<CancellationToken>()).Returns((SiteAnalysisProjectLink?)null);
        _executionRepository.ListCompletedStepsByUserChatIdAsync(chatId, Arg.Any<CancellationToken>()).Returns([]);
        _sender.Send(Arg.Any<StartAgentExecutionCommand>(), Arg.Any<CancellationToken>())
            .Returns(new AgentExecutionSummaryDto(Guid.NewGuid(), ConfiguredAgentId, "Queued", false, DateTime.UtcNow) { HangfireJobId = "hf-job-1" });

        await CreateRouter().HandleUserMessageAsync(chatId, "I want to redesign Al Safa Park in Dubai.", CancellationToken.None);

        await _sender.Received(1).Send(
            Arg.Is<StartAgentExecutionCommand>(c =>
                c.AgentId == ConfiguredAgentId
                && c.ConversationIntegrationMode == AgentConversationIntegrationMode.ExistingConversation
                && c.UserChatId == chatId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleUserMessage_ShouldTreatAConfirmationReply_AsLinkingRatherThanANewSiteDescription()
    {
        var chatId = Guid.NewGuid();
        _linkRepository.GetByUserChatIdAsync(chatId, Arg.Any<CancellationToken>()).Returns((SiteAnalysisProjectLink?)null);

        var execution = AgentExecution.Create(
            ConfiguredAgentId, Guid.NewGuid(), "user-1", "Resolve the site boundary for Al Safa Park", false,
            AgentConversationIntegrationMode.ExistingConversation, chatId, "user-1");
        var step = execution.AddStep(0, "Resolve boundary", AgentExecutionStepType.ToolCall, null, SiteAnalysisToolNames.ResolveSiteBoundary, null);
        step.Start();
        step.Complete("""{"resolved":true,"builtAssetConfirmed":true,"resolvedName":"Al Safa Park","latitude":25.15,"longitude":55.22,"candidateCount":1}""");

        _executionRepository.ListCompletedStepsByUserChatIdAsync(chatId, Arg.Any<CancellationToken>()).Returns([step]);
        _userChatRepository.GetByIdAsync(chatId, Arg.Any<CancellationToken>())
            .Returns(UserChat.Create("Al Safa Park session", "user-1", null, "user-1"));
        _sender.Send(Arg.Any<VerifyAndLinkDigitalCoreProjectCommand>(), Arg.Any<CancellationToken>())
            .Returns(new VerifyAndLinkDigitalCoreProjectResult(VerifyAndLinkDigitalCoreProjectOutcome.Created, "tdc-new", null));

        await CreateRouter().HandleUserMessageAsync(chatId, "yes, go ahead", CancellationToken.None);

        await _sender.DidNotReceive().Send(Arg.Any<StartAgentExecutionCommand>(), Arg.Any<CancellationToken>());
        await _sender.Received(1).Send(
            Arg.Is<VerifyAndLinkDigitalCoreProjectCommand>(c =>
                c.UserChatId == chatId && c.SiteName == "Al Safa Park" && c.UserConfirmedCreate),
            Arg.Any<CancellationToken>());
    }
}
