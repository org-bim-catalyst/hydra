using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Commands.StartAgentExecution;
using AskLucy.Application.Options;
using AskLucy.Application.SiteAnalysis.AgentExecutionCompletion;
using AskLucy.Application.SiteAnalysis.Commands.VerifyAndLinkDigitalCoreProject;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Chats;
using Hangfire;
using MediatR;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.SiteAnalysis.Routing;

/// <summary>
/// The one new mechanism this feature introduces (Clarifications Q1, contracts/chat-to-agent-routing.md):
/// recognizes a qualifying chat message in a site-analysis conversation and starts a new, short
/// <c>AgentExecution</c> against the one pre-published Site Analysis Agent (research.md Decisions
/// 1-2) \u2014 or, if the message is a bootstrap confirmation reply, calls
/// <see cref="VerifyAndLinkDigitalCoreProjectCommand"/> directly. Invoked from
/// <see cref="SiteAnalysisChatTurnBehavior"/> after every user-authored <c>AppendMessageCommand</c>
/// succeeds \u2014 never a new controller/endpoint, never a change to the Agent Engine's core
/// mechanisms.
///
/// <para>Deliberately scoped to only this feature's own conversations (a
/// <c>SiteAnalysisProjectLink</c> must already exist, or the message must look like a fresh site
/// description) and only this feature's own narrow trigger phrases \u2014 not a general-purpose
/// platform-wide intent classifier (research.md Decision 1's YAGNI rationale).</para>
/// </summary>
public sealed class SiteAnalysisChatTurnRouter(
    ISiteAnalysisProjectLinkRepository linkRepository,
    SiteAnalysisConversationStateAssembler stateAssembler,
    IMessageRepository messageRepository,
    IUserChatRepository userChatRepository,
    ISender sender,
    IOptions<SiteAnalysisOptions> options,
    IBackgroundJobClient backgroundJobClient,
    IUnitOfWork unitOfWork)
{
    private const string SystemActor = "system:site-analysis-runtime";

    private static readonly string[] ConfirmationPhrases =
        ["yes", "yeah", "yep", "confirm", "go ahead", "create it", "do it", "please create", "sounds good"];

    public async Task HandleUserMessageAsync(Guid userChatId, string content, CancellationToken cancellationToken)
    {
        if (options.Value.AgentId == Guid.Empty)
        {
            return; // The Site Analysis Agent hasn't been provisioned yet (T016 dev-seed) — no-op rather than failing every message.
        }

        var link = await linkRepository.GetByUserChatIdAsync(userChatId, cancellationToken);
        var state = await stateAssembler.AssembleAsync(userChatId, cancellationToken);

        if (link is null)
        {
            if (state.LastResolvedBoundaryStep is not null && LooksLikeConfirmation(content))
            {
                await HandleBootstrapConfirmationAsync(userChatId, state.LastResolvedBoundaryStep, cancellationToken);
                return;
            }

            await StartBoundaryResolutionAsync(userChatId, content, cancellationToken);
            return;
        }

        // US2+: conversation already linked to a TheDigitalCore Project.
        if (state.LastResolvedBoundaryStep is not null)
        {
            return; // FR-005: boundary already resolved — reused, not re-resolved. Category requests (US3+) are not yet handled by this router.
        }

        await StartBoundaryResolutionAsync(userChatId, content, cancellationToken);
    }

    private async Task StartBoundaryResolutionAsync(Guid userChatId, string content, CancellationToken cancellationToken)
    {
        var objective = $"Resolve the site boundary and built-asset status for: {content}";
        var summary = await sender.Send(
            new StartAgentExecutionCommand(
                options.Value.AgentId, AgentVersionNumber: null, objective,
                AgentConversationIntegrationMode.ExistingConversation, userChatId, IsTestExecution: false),
            cancellationToken);

        if (summary.HangfireJobId is { } jobId)
        {
            backgroundJobClient.ContinueJobWith<ISiteAnalysisCompletionReactionJob>(
                jobId, j => j.ProcessAsync(summary.Id, CancellationToken.None));
        }
    }

    private async Task HandleBootstrapConfirmationAsync(Guid userChatId, AgentExecutionStep boundaryStep, CancellationToken cancellationToken)
    {
        var boundary = ResolvedBoundaryDto.Parse(boundaryStep.OutputJson);
        if (boundary is null || !boundary.Resolved)
        {
            return;
        }

        var result = await sender.Send(
            new VerifyAndLinkDigitalCoreProjectCommand(
                userChatId, boundary.ResolvedName ?? "Unnamed site", boundary.Latitude, boundary.Longitude, UserConfirmedCreate: true),
            cancellationToken);

        var reply = result.Outcome == VerifyAndLinkDigitalCoreProjectOutcome.Created
            ? $"Done — I've created \"{boundary.ResolvedName}\" as a new project in TheDigitalCore and linked this conversation to it."
            : "I wasn't able to create the project just now — please try again.";

        var chat = await userChatRepository.GetByIdAsync(userChatId, cancellationToken);
        if (chat is null)
        {
            return;
        }

        messageRepository.Add(Message.Create(userChatId, MessageRole.Assistant, MessageKind.Text, reply, sourceText: null, SystemActor));
        chat.TouchLastActivity(SystemActor);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static bool LooksLikeConfirmation(string content) =>
        ConfirmationPhrases.Any(phrase => content.Contains(phrase, StringComparison.OrdinalIgnoreCase));
}
