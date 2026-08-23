namespace AskLucy.Application.SiteAnalysis;

/// <summary>Registered MCP tool names (contracts/site-analysis-mcp-tools.md) \u2014 must match the `@mcp.tool()` function names in `park-redesign/mcp_server/server.py`.</summary>
public static class SiteAnalysisToolNames
{
    public const string ResolveSiteBoundary = "resolve_site_boundary_tool";
    public const string CollectRecreationDataLayers = "collect_recreation_data_layers_tool";
    public const string CollectSocialDataLayers = "collect_social_data_layers_tool";
    public const string ScoreRecreation = "score_recreation_tool";
    public const string ScoreSocial = "score_social_tool";
}
