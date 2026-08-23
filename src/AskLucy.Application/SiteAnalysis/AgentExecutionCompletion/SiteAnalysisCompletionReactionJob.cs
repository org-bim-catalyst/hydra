using AskLucy.Application.Abstractions;
using AskLucy.Application.Panels;
using AskLucy.Application.SiteAnalysis.Commands.VerifyAndLinkDigitalCoreProject;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Chats;
using MediatR;

namespace AskLucy.Application.SiteAnalysis.AgentExecutionCompletion;

/// <summary>
/// Handles the completion of a <c>resolve_site_boundary</c>-driven execution (FR-001a, FR-001g,
/// FR-002, FR-003, FR-004): reads the tool's output directly from
/// <c>AgentExecutionStep.OutputJson</c> (no new persisted result table, per research.md Decision
/// 6's spirit — <c>FinalOutputJson</c> itself turned out to be hardcoded to a citations wrapper by
/// the orchestrator, discovered during implementation), then either asks for clarification
/// (unresolvable location, or an ambiguous multi-candidate match — FR-004), renders the boundary
/// (FR-003, pushed via the existing <see cref="IPanelNotifier"/> — the Immersive Viewer's command
/// API turned out to be a client-side-only TypeScript surface with no backend push channel of its
/// own, discovered during implementation; the frontend renderer for the
/// <c>site-analysis-boundary</c> panel type forwards to the real <c>addLayer</c>/<c>zoomToLocation</c>
/// viewer commands instead of opening a floating window — see
/// <c>ClientApp/src/viewer/panels/types/site-analysis-boundary/SiteAnalysisBoundaryPanel.tsx</c>), and — only
/// when this conversation has no <c>SiteAnalysisProjectLink</c> yet — runs the bootstrap
/// search/offer/link flow (FR-001c-FR-001f). Posts the reply directly via
/// <see cref="IMessageRepository"/> since this runs in a Hangfire background job with no HTTP
/// context (mirrors <c>AgentExecutionOrchestrator.PostResultToConversationAsync</c>'s own
/// constraint).
/// </summary>
public sealed class SiteAnalysisCompletionReactionJob(
    IAgentExecutionRepository executionRepository,
    ISiteAnalysisProjectLinkRepository linkRepository,
    IUserChatRepository userChatRepository,
    IMessageRepository messageRepository,
    IPanelNotifier panelNotifier,
    ISender sender,
    IUnitOfWork unitOfWork) : ISiteAnalysisCompletionReactionJob
{
    private const string SystemActor = "system:site-analysis-runtime";
    private const string BoundaryPanelTypeKey = "site-analysis-boundary";

    public async Task ProcessAsync(Guid agentExecutionId, CancellationToken cancellationToken = default)
    {
        var execution = await executionRepository.GetByIdAsync(agentExecutionId, cancellationToken);
        if (execution is null || execution.UserChatId is not { } userChatId || execution.Status != AgentExecutionStatus.Completed)
        {
            // Failed/cancelled/standalone executions surface their own error through the existing
            // AgentExecutionError/chat-post path \u2014 nothing extra for this reaction to do (constitution §2.VIII: the failure itself is never silent, just not this job's concern).
            return;
        }

        var boundaryStep = execution.Steps.FirstOrDefault(
            s => s.ToolName == SiteAnalysisToolNames.ResolveSiteBoundary && s.Status == AgentExecutionStepStatus.Completed);
        if (boundaryStep is null)
        {
            return; // Not a boundary-resolution execution (e.g. a future category-analysis execution, US3+).
        }

        var boundary = ResolvedBoundaryDto.Parse(boundaryStep.OutputJson);
        if (boundary is null)
        {
            return;
        }

        string reply;
        var existingLink = await linkRepository.GetByUserChatIdAsync(userChatId, cancellationToken);

        if (!boundary.Resolved)
        {
            // FR-001b: location itself could not be resolved — ask for clarification, never search/create.
            reply = $"I couldn't confidently resolve that location ({boundary.Reason ?? "no match found"}). Could you clarify the site name or provide coordinates?";
        }
        else if (boundary.CandidateCount > 1)
        {
            // FR-004: more than one plausible real-world location — ask, never guess.
            reply = $"I found more than one place that could match \"{boundary.ResolvedName}\" — could you be more specific (e.g. add the city)?";
        }
        else
        {
            await DispatchBoundaryPanelAsync(execution.RunByUserId, boundary, cancellationToken);

            if (existingLink is not null)
            {
                // US2: already linked to a Project — just render the boundary, no bootstrap search/offer.
                reply = $"Here's {boundary.ResolvedName} — I've centered the view on it.";
            }
            else
            {
                // US1: no Project linked yet — run the bootstrap search/offer/link flow (FR-001c-FR-001f).
                var result = await sender.Send(
                    new VerifyAndLinkDigitalCoreProjectCommand(
                        userChatId, boundary.ResolvedName ?? "Unnamed site", boundary.Latitude, boundary.Longitude, UserConfirmedCreate: false),
                    cancellationToken);

                var statusNote = boundary.BuiltAssetConfirmed
                    ? "I found the physical asset."
                    : "This looks like a planned or proposed site rather than a confirmed existing asset."; // FR-001g

                reply = result.Outcome switch
                {
                    VerifyAndLinkDigitalCoreProjectOutcome.LinkedToExisting =>
                        $"{statusNote} I found an existing digital project for it in TheDigitalCore and linked this conversation to it.",
                    VerifyAndLinkDigitalCoreProjectOutcome.AmbiguousCandidates =>
                        $"{statusNote} I found more than one possible matching project in TheDigitalCore — could you confirm which one (if any) is this site?",
                    _ => // AwaitingCreateConfirmation
                        $"{statusNote} I couldn't find an existing digital project for it. Would you like me to create one?",
                };
            }
        }

        var chat = await userChatRepository.GetByIdAsync(userChatId, cancellationToken);
        if (chat is null)
        {
            return; // The conversation was deleted mid-execution — nothing to post into.
        }

        messageRepository.Add(Message.Create(userChatId, MessageRole.Assistant, MessageKind.Text, reply, sourceText: null, SystemActor));
        chat.TouchLastActivity(SystemActor);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private Task DispatchBoundaryPanelAsync(string userId, ResolvedBoundaryDto boundary, CancellationToken cancellationToken) =>
        panelNotifier.PanelRequestedAsync(
            userId,
            new PanelRequestDto(
                RequestId: Guid.CreateVersion7().ToString(),
                TypeKey: BoundaryPanelTypeKey,
                Title: boundary.ResolvedName ?? "Site boundary",
                Data: new
                {
                    boundary.ResolvedName,
                    boundary.Latitude,
                    boundary.Longitude,
                    boundary.BuiltAssetConfirmed,
                },
                Position: null,
                ContextAssociation: null),
            cancellationToken);
}
