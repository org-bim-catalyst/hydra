using AskLucy.Domain.Common;

namespace AskLucy.Domain.Agents;

/// <summary>Estimated monetary cost for one execution (spec.md FR-036, data-model.md) — derived from <see cref="AgentExecutionUsage"/> via the existing <c>ModelPricing</c> lookup, no new pricing logic.</summary>
public sealed class AgentExecutionCost : BaseEntity
{
    public Guid AgentExecutionId { get; private set; }

    public decimal EstimatedCost { get; private set; }

    public string Currency { get; private set; } = "USD";

    private AgentExecutionCost()
    {
        // Required by EF Core materialization.
    }

    public static AgentExecutionCost Create(Guid agentExecutionId, decimal estimatedCost, string currency) => new()
    {
        Id = Guid.CreateVersion7(),
        AgentExecutionId = agentExecutionId,
        EstimatedCost = estimatedCost,
        Currency = currency,
        CreatedAtUtc = DateTime.UtcNow,
    };

    public void AddCost(decimal additionalCost)
    {
        EstimatedCost += additionalCost;
        ModifiedAtUtc = DateTime.UtcNow;
    }
}
