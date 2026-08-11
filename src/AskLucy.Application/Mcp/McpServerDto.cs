using AskLucy.Domain.Mcp;

namespace AskLucy.Application.Mcp;

/// <summary>FR-045 — never includes credential material, at any point.</summary>
public sealed record McpServerDto(
    Guid Id, string Name, string? Description, string Endpoint, McpServerTransport Transport,
    McpAuthenticationType AuthenticationType, bool RequiresUnauthenticatedConfirmation,
    bool AllowInsecureTransport, string? InsecureTransportJustification,
    bool EndpointValidationOverride, string? EndpointValidationJustification,
    bool IsEnabled, string OwnerUserId, int ConfigurationVersion, int CapabilityRefreshIntervalMinutes,
    DateTime? LastHealthCheckAtUtc, DateTime? LastCapabilityDiscoveryAtUtc,
    DateTime CreatedAtUtc, DateTime? ModifiedAtUtc)
{
    public static McpServerDto Create(McpServer server) => new(
        server.Id, server.Name, server.Description, server.Endpoint, server.Transport,
        server.AuthenticationType, server.RequiresUnauthenticatedConfirmation,
        server.AllowInsecureTransport, server.InsecureTransportJustification,
        server.EndpointValidationOverride, server.EndpointValidationJustification,
        server.IsEnabled, server.OwnerUserId, server.ConfigurationVersion, server.CapabilityRefreshIntervalMinutes,
        server.LastHealthCheckAtUtc, server.LastCapabilityDiscoveryAtUtc,
        server.CreatedAtUtc, server.ModifiedAtUtc);
}

public sealed record McpServerHealthDto(Guid McpServerId, McpServerHealthStatus Status, McpFailureCategory? FailureCategory, string? Detail, DateTime CheckedAtUtc, int ConsecutiveFailureCount)
{
    public static McpServerHealthDto Create(McpServerHealth health) => new(
        health.McpServerId, health.Status, health.FailureCategory, health.Detail, health.CheckedAtUtc, health.ConsecutiveFailureCount);
}

public sealed record McpToolDto(
    Guid Id, Guid McpServerId, string NamespacedName, string ToolName, string DisplayName, string Description,
    string InputSchemaJson, string OutputSchemaJson, AgentToolRiskLevelDto? ServerDeclaredRiskLevel,
    AgentToolRiskLevelDto EffectiveRiskLevel, IReadOnlyList<string> RequiredPermissions,
    McpToolActivationStatus ActivationStatus, string? ActivatedByUserId, DateTime? ActivatedAtUtc,
    string? Version, bool IsAvailable)
{
    public static McpToolDto Create(McpTool tool) => new(
        tool.Id, tool.McpServerId, tool.NamespacedName, tool.ToolName, tool.DisplayName, tool.Description,
        tool.InputSchemaJson, tool.OutputSchemaJson,
        tool.ServerDeclaredRiskLevel is { } declared ? (AgentToolRiskLevelDto)declared : null,
        (AgentToolRiskLevelDto)tool.EffectiveRiskLevel,
        System.Text.Json.JsonSerializer.Deserialize<List<string>>(tool.RequiredPermissionsJson) ?? [],
        tool.ActivationStatus, tool.ActivatedByUserId, tool.ActivatedAtUtc, tool.Version, tool.IsAvailable);
}

/// <summary>Mirrors <c>Domain.Agents.AgentToolRiskLevel</c>'s values — a separate DTO-layer enum so <c>AskLucy.Application.Mcp</c> doesn't force every consumer to reference <c>AskLucy.Domain.Agents</c> just to read a risk level.</summary>
public enum AgentToolRiskLevelDto
{
    Low,
    Medium,
    High,
    Critical,
}

public sealed record McpAuditLogDto(Guid Id, Guid? McpServerId, string UserId, McpAuditAction Action, McpFailureCategory? FailureCategory, string DetailsJson, DateTime OccurredAtUtc)
{
    public static McpAuditLogDto Create(McpAuditLog entry) => new(
        entry.Id, entry.McpServerId, entry.UserId, entry.Action, entry.FailureCategory, entry.DetailsJson, entry.OccurredAtUtc);
}

public sealed record McpServerReferenceDto(Guid AgentId, string ToolName);
