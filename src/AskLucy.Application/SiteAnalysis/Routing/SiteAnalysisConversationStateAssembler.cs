using AskLucy.Application.Abstractions;
using AskLucy.Domain.Agents;

namespace AskLucy.Application.SiteAnalysis.Routing;

/// <summary>What is already known about a site-analysis conversation, reconstructed from existing
/// `AgentExecutionStep` audit data rather than new persisted state (research.md Decision 1/plan.md
/// Post-Design Constitution Check note 2).</summary>
public sealed record SiteAnalysisConversationState(AgentExecutionStep? LastResolvedBoundaryStep);

/// <summary>Assembles <see cref="SiteAnalysisConversationState"/> for a `UserChat` — shared by
/// <see cref="SiteAnalysisChatTurnRouter"/> and `SiteAnalysisCompletionReactionJob` so the "what
/// has already happened in this conversation" query lives in exactly one place.</summary>
public sealed class SiteAnalysisConversationStateAssembler(IAgentExecutionRepository executionRepository)
{
    public async Task<SiteAnalysisConversationState> AssembleAsync(Guid userChatId, CancellationToken cancellationToken)
    {
        var recentSteps = await executionRepository.ListCompletedStepsByUserChatIdAsync(userChatId, cancellationToken);
        var lastBoundaryStep = recentSteps.FirstOrDefault(s => s.ToolName == SiteAnalysisToolNames.ResolveSiteBoundary);
        return new SiteAnalysisConversationState(lastBoundaryStep);
    }
}
