using AskLucy.Domain.Common;

namespace AskLucy.Domain.Agents;

/// <summary>
/// Draft-time association between an <see cref="Agent"/> and a Knowledge Base it may search
/// (spec.md FR-029, data-model.md). Access is still re-validated per-execution against the
/// caller's own authorization (FR-049) — this row expresses configuration, not a standing grant.
/// </summary>
public sealed class AgentKnowledgeBase : BaseEntity
{
    public Guid AgentId { get; private set; }

    public Guid KnowledgeBaseId { get; private set; }

    private AgentKnowledgeBase()
    {
        // Required by EF Core materialization.
    }

    internal static AgentKnowledgeBase Create(Guid agentId, Guid knowledgeBaseId, string actor) => new()
    {
        Id = Guid.CreateVersion7(),
        AgentId = agentId,
        KnowledgeBaseId = knowledgeBaseId,
        CreatedAtUtc = DateTime.UtcNow,
        CreatedBy = actor,
    };
}
