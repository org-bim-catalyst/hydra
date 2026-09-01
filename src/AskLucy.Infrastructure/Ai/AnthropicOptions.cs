namespace AskLucy.Infrastructure.Ai;

/// <summary>Bound from configuration/environment — never hardcoded (constitution §8/§22).</summary>
public sealed class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    public required string ApiKey { get; init; }

    public string ChatModel { get; init; } = "claude-3-5-sonnet-20241022";

    public string BaseUrl { get; init; } = "https://api.anthropic.com/v1/";

    /// <summary>Anthropic requires this header on every request (research.md Decision 1).</summary>
    public string ApiVersion { get; init; } = "2023-06-01";

    /// <summary>
    /// Workspace this key acts in, sent as <c>anthropic-workspace-id</c>. Empty for a classic
    /// API key, which needs no workspace.
    /// </summary>
    /// <remarks>
    /// Anthropic's newer <i>identity-linked</i> keys refuse every request without it, with a 400
    /// whose message says so outright: "anthropic-workspace-id is required when authenticating
    /// with an identity-linked API key; send the id of the workspace this request acts in."
    /// Observed in production 2026-09-01, which left Anthropic unusable and its health card
    /// reading "rejected this request as invalid".
    /// </remarks>
    public string WorkspaceId { get; init; } = string.Empty;
}
