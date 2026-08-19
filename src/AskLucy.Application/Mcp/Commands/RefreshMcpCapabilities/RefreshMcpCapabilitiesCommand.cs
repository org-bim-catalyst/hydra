using MediatR;

namespace AskLucy.Application.Mcp.Commands.RefreshMcpCapabilities;

/// <summary>spec.md FR-011/FR-013/FR-015/FR-016 — admin-triggered here; <c>McpCapabilityRefreshJob</c> (US6) calls the same handler on a schedule (research.md Decision 10).</summary>
public sealed record RefreshMcpCapabilitiesCommand(Guid Id) : IRequest<McpCapabilityRefreshResultDto>;

public sealed record McpCapabilityRefreshResultDto(bool WasSuccessful, string? ChangeSummaryJson, int ToolCount, int ResourceCount, int PromptCount);
