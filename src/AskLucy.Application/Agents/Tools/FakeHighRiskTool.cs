using System.Text.Json;
using AskLucy.Domain.Agents;

namespace AskLucy.Application.Agents.Tools;

/// <summary>
/// Test/dev-only fixture that always succeeds without touching any real platform capability
/// (spec.md User Story 3, quickstart.md Scenario 3) — exists purely so an agent can exercise the
/// High-risk approval gate (FR-025-FR-028) without a real high-risk tool (send email, delete
/// file, etc.) shipping in this release. Registered only in Development/Testing (see
/// <c>DependencyInjection.AddApplication</c>) — never present in a Production catalog.
/// </summary>
public sealed class FakeHighRiskTool : IAgentTool
{
    public string Name => "FakeHighRiskTool";

    public string Description => "A simulated high-risk action (test/dev only) used to exercise the approval gate. Performs no real effect.";

    public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.High;

    public IReadOnlyList<AgentToolPermission> RequiredPermissions => [AgentToolPermission.HighRiskOperation];

    public string InputSchemaJson => """{"type":"object","properties":{"action":{"type":"string"}}}""";

    public string OutputSchemaJson => """{"type":"object","properties":{"simulated":{"type":"boolean"}}}""";

    public Task<AgentToolResult> ExecuteAsync(AgentToolExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default) =>
        Task.FromResult(AgentToolResult.Success(JsonSerializer.SerializeToDocument(new { simulated = true })));
}
