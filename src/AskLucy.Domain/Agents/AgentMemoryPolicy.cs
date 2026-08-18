using AskLucy.Domain.Common;

namespace AskLucy.Domain.Agents;

/// <summary>
/// One-to-one with <see cref="Agent"/> — governs whether/how an agent may read memory or
/// propose memory writes (spec.md FR-030/FR-031, data-model.md). Final admission of any
/// proposed write still runs through the Memory Engine's own <c>PendingApproval</c> lifecycle
/// (research.md Decision 5); this policy only governs whether the agent may call the Memory
/// Search/Memory Write tools at all.
/// </summary>
public sealed class AgentMemoryPolicy : BaseEntity
{
    public Guid AgentId { get; private set; }

    public bool AllowRead { get; private set; }

    public bool AllowWriteProposals { get; private set; }

    public string? PreApprovedCategoriesJson { get; private set; }

    private AgentMemoryPolicy()
    {
        // Required by EF Core materialization.
    }

    internal static AgentMemoryPolicy Create(Guid agentId, bool allowRead, bool allowWriteProposals, string? preApprovedCategoriesJson, string actor) => new()
    {
        Id = Guid.CreateVersion7(),
        AgentId = agentId,
        AllowRead = allowRead,
        AllowWriteProposals = allowWriteProposals,
        PreApprovedCategoriesJson = preApprovedCategoriesJson,
        CreatedAtUtc = DateTime.UtcNow,
        CreatedBy = actor,
    };
}
