using AskLucy.Domain.Mcp;

namespace AskLucy.Application.Mcp;

/// <summary>contracts/mcp-api.md's `GET /mcp/catalog/tools` shape — deliberately slimmer than the admin `McpToolDto` (no `Id`/`McpServerId`/activation metadata a non-admin user has no use for).</summary>
public sealed record McpToolCatalogSummaryDto(
    string NamespacedName, string DisplayName, string Description, string SourceServerName,
    AgentToolRiskLevelDto EffectiveRiskLevel, IReadOnlyList<string> RequiredPermissions)
{
    public static McpToolCatalogSummaryDto Create(McpTool tool, string serverName) => new(
        tool.NamespacedName, tool.DisplayName, tool.Description, serverName,
        (AgentToolRiskLevelDto)tool.EffectiveRiskLevel,
        System.Text.Json.JsonSerializer.Deserialize<List<string>>(tool.RequiredPermissionsJson) ?? []);
}

/// <summary>contracts/mcp-api.md's `GET /mcp/catalog/tools/{namespacedName}` shape (FR-020) — full input/output schema, capabilities, version, last-updated.</summary>
public sealed record McpToolDetailDto(
    string NamespacedName, string DisplayName, string Description, string SourceServerName,
    string InputSchemaJson, string OutputSchemaJson, string? DeclaredCapabilitiesJson,
    AgentToolRiskLevelDto EffectiveRiskLevel, IReadOnlyList<string> RequiredPermissions,
    string? Version, DateTime? LastUpdatedAtUtc)
{
    public static McpToolDetailDto Create(McpTool tool, string serverName) => new(
        tool.NamespacedName, tool.DisplayName, tool.Description, serverName,
        tool.InputSchemaJson, tool.OutputSchemaJson, tool.DeclaredCapabilitiesJson,
        (AgentToolRiskLevelDto)tool.EffectiveRiskLevel,
        System.Text.Json.JsonSerializer.Deserialize<List<string>>(tool.RequiredPermissionsJson) ?? [],
        tool.Version, tool.ModifiedAtUtc ?? tool.CreatedAtUtc);
}

/// <summary>contracts/mcp-api.md's `GET /mcp/catalog/resources` shape (FR-036).</summary>
public sealed record McpResourceCatalogSummaryDto(string NamespacedName, string Uri, string Name, string? Description, string? ContentType, string SourceServerName)
{
    public static McpResourceCatalogSummaryDto Create(McpResource resource, string serverName) => new(
        resource.NamespacedName, resource.Uri, resource.Name, resource.Description, resource.ContentType, serverName);
}

/// <summary>contracts/mcp-api.md's `GET /mcp/catalog/prompts` shape (FR-042) — MCP-sourced prompts only; merged with native `Prompt`s client-side wherever a unified prompt picker is shown (research.md Decision 16).</summary>
public sealed record McpPromptCatalogSummaryDto(string NamespacedName, string Name, string? Description, string SourceServerName)
{
    public static McpPromptCatalogSummaryDto Create(McpPrompt prompt, string serverName) => new(
        prompt.NamespacedName, prompt.Name, prompt.Description, serverName);
}
