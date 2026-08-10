using System.Text.Json;
using AskLucy.Application.Memory.Commands.CreateMemoryCandidate;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Memory;
using MediatR;

namespace AskLucy.Application.Agents.Tools;

/// <summary>
/// Proposes a memory candidate (spec.md FR-024/FR-031, research.md Decision 5) — creates a
/// <c>PendingApproval</c> row via <see cref="CreateMemoryCandidateCommand"/>, never writes
/// directly. Medium risk, not High/Critical (contracts/agent-tool-contract.md): admission into
/// Active memory always still passes through the Memory Engine's own approval gate, so this tool
/// creates a proposal, not an immediate mutation. Safe to call via <see cref="ISender"/> even from
/// the background orchestrator because <see cref="CreateMemoryCandidateCommand"/> takes the user
/// id explicitly rather than depending on <c>ICurrentUserAccessor</c>.
/// </summary>
public sealed class MemoryWriteTool(ISender sender) : IAgentTool
{
    public string Name => "MemoryWriteTool";

    public string Description => "Proposes a new long-term memory about the user. The proposal requires approval before it becomes active — this never writes memory directly.";

    public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Medium;

    public IReadOnlyList<AgentToolPermission> RequiredPermissions => [AgentToolPermission.ModifyData];

    public string InputSchemaJson => """{"type":"object","required":["content","category"],"properties":{"content":{"type":"string"},"category":{"type":"string","enum":["UserPreference","PersonalFact","ProjectContext","ConversationDerived"]},"isSensitive":{"type":"boolean"}}}""";

    public string OutputSchemaJson => """{"type":"object","properties":{"memoryId":{"type":["string","null"]}}}""";

    public async Task<AgentToolResult> ExecuteAsync(AgentToolExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
    {
        if (!input.RootElement.TryGetProperty("content", out var contentElement) || contentElement.GetString() is not { Length: > 0 } content)
        {
            return AgentToolResult.Failure("Non-empty memory content is required.");
        }

        if (!input.RootElement.TryGetProperty("category", out var categoryElement) ||
            !Enum.TryParse<MemoryCategory>(categoryElement.GetString(), ignoreCase: true, out var category))
        {
            return AgentToolResult.Failure("A valid memory category is required.");
        }

        var isSensitive = input.RootElement.TryGetProperty("isSensitive", out var sensitiveElement) && sensitiveElement.GetBoolean();

        var memoryId = await sender.Send(
            new CreateMemoryCandidateCommand(context.UserId, ProjectId: null, category, content, Importance: 0.6m, Confidence: 0.8m, isSensitive),
            cancellationToken);

        return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new { memoryId }));
    }
}
