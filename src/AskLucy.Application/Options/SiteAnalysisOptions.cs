namespace AskLucy.Application.Options;

/// <summary>
/// Bound from configuration (constitution &#167;4). <see cref="AgentId"/> identifies the one
/// pre-published "Site Analysis Agent" (research.md Decision 2, T016 dev-seed) that
/// <c>SiteAnalysisChatTurnRouter</c> starts every qualifying turn's <c>AgentExecution</c> against
/// \u2014 set once an administrator has created and published that agent; empty until then.
/// </summary>
public sealed class SiteAnalysisOptions
{
    public const string SectionName = "SiteAnalysis";

    public Guid AgentId { get; init; }
}
