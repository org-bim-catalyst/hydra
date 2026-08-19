using AskLucy.Domain.Common;

namespace AskLucy.Domain.Workflows;

/// <summary>Estimated monetary cost for one execution (FR-054, data-model.md) — derived from <see cref="WorkflowExecutionUsage"/> via the existing <c>ModelPricing</c> lookup, no new pricing logic.</summary>
public sealed class WorkflowExecutionCost : BaseEntity
{
    public Guid WorkflowExecutionId { get; private set; }

    public decimal EstimatedCost { get; private set; }

    public string CurrencyCode { get; private set; } = "USD";

    private WorkflowExecutionCost()
    {
        // Required by EF Core materialization.
    }

    public static WorkflowExecutionCost Create(Guid workflowExecutionId, decimal estimatedCost, string currencyCode) => new()
    {
        Id = Guid.CreateVersion7(),
        WorkflowExecutionId = workflowExecutionId,
        EstimatedCost = estimatedCost,
        CurrencyCode = currencyCode,
        CreatedAtUtc = DateTime.UtcNow,
    };

    public void AddCost(decimal additionalCost)
    {
        EstimatedCost += additionalCost;
        ModifiedAtUtc = DateTime.UtcNow;
    }
}
