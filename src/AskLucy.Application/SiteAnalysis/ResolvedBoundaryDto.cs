using System.Text.Json;
using System.Text.Json.Serialization;

namespace AskLucy.Application.SiteAnalysis;

/// <summary>Parsed `resolve_site_boundary` tool output (contracts/site-analysis-mcp-tools.md), read from <c>AgentExecutionStep.OutputJson</c>.</summary>
public sealed class ResolvedBoundaryDto
{
    public bool Resolved { get; init; }

    [JsonPropertyName("builtAssetConfirmed")]
    public bool BuiltAssetConfirmed { get; init; }

    [JsonPropertyName("resolvedName")]
    public string? ResolvedName { get; init; }

    public decimal? Latitude { get; init; }

    public decimal? Longitude { get; init; }

    [JsonPropertyName("candidateCount")]
    public int CandidateCount { get; init; }

    public string? Reason { get; init; }

    public static ResolvedBoundaryDto? Parse(string? outputJson)
    {
        if (string.IsNullOrWhiteSpace(outputJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ResolvedBoundaryDto>(outputJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
