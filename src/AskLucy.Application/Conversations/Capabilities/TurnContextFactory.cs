using AskLucy.Application.Agents.Tools;
using AskLucy.Domain.Chats;

namespace AskLucy.Application.Conversations.Capabilities;

/// <summary>
/// Builds a <see cref="TurnContext"/> from the pieces every caller already has to hand — a chat's
/// active location/boundary and its attached knowledge bases (specs/045 FR-011).
/// <para>
/// Extracted from <c>ConversationTurnOrchestrator</c>'s own private method (specs/045 Phase 5,
/// T071) so <see cref="Runtime.SelectedActionResolver"/> can build the exact same snapshot to
/// re-check availability at dispatch time, without either duplicating this logic or depending on
/// the orchestrator itself.
/// </para>
/// </summary>
public static class TurnContextFactory
{
    public static TurnContext Build(
        string? userId,
        Guid userChatId,
        ActiveSiteLocation? activeLocation,
        ActiveSiteBoundary? activeBoundary,
        IReadOnlyList<Guid> knowledgeBaseIds)
    {
        // specs/045 FR-011 rule 3 — entitlement is meant to be enforced centrally, against a real
        // per-user grant. No granular permission system surfaces to chat today (the pre-existing
        // pipeline ran retrieval/location/memory for any authenticated user unconditionally), so
        // every authenticated user is granted the low-risk permissions the built-in capabilities
        // declare — Low risk is exactly the tier none of them exceed. An unauthenticated turn
        // (userId null) is granted none, which is the safer default. Documented here rather than
        // silently assumed: a real entitlement source is a genuine gap, not an oversight.
        var granted = userId is null
            ? new HashSet<AgentToolPermission>()
            : new HashSet<AgentToolPermission>
            {
                AgentToolPermission.ExternalNetwork,
                AgentToolPermission.ReadKnowledge,
                AgentToolPermission.ReadMemory,
            };

        return new TurnContext(
            userId,
            userChatId,
            activeLocation,
            activeBoundary,
            knowledgeBaseIds,
            HasAttachedDocuments: false,
            IsMemoryAvailable: userId is not null,
            OpenPanelTypeKeys: [],
            granted,
            SubscriptionTier: null);
    }
}
