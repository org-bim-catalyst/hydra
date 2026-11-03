using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.SiteAnalysis.Providers;
using AskLucy.Domain.Agents;
using AskLucy.Domain.SiteAnalysis;
using Microsoft.Extensions.DependencyInjection;
using SiteAnalysisAggregate = AskLucy.Domain.SiteAnalysis.SiteAnalysis;

namespace AskLucy.Application.SiteAnalysis.Tools;

/// <summary>
/// specs/057-site-analysis-agent — the character analysis specialist. It reads zoning and
/// urban-character data for a resolved site and reports it to the site analysis coordinator.
/// Where a figure has no data source behind it, the finding states that it is unavailable
/// rather than presenting an invented value (FR-014). Confidence is rule-based via
/// <see cref="SiteAnalysisConfidence.ResolveConfidence"/> from grounding facts only,
/// never from any geocoding Importance field (research.md D10).
/// </summary>
public sealed class SiteCharacterAnalysisTool(
    IServiceScopeFactory scopeFactory,
    SiteAnalysisContentComposer composer,
    ISiteAnalysisResultRelay relay,
    IZoningDataProvider zoningDataProvider) : IAgentTool
{
    public const string ToolName = "site_analysis_character_analysis";

    private const string FindingHeading = "Character Analysis";
    private const string NoZoningDataText = "Zoning data is unavailable";
    private const string FARText = "Floor area ratio";
    private const string SetbackText = "Setback";
    private const string HeightLimitText = "Height limit";

    public string Name => ToolName;

    public string Description =>
        "Reports zoning and urban-character data for a resolved site and reports it to the site analysis coordinator.";

    public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Low;

    public IReadOnlyList<AgentToolPermission> RequiredPermissions => [];

    public string InputSchemaJson =>
        """{"type":"object","required":["siteAnalysisId","siteName","siteLocation"],"properties":{"siteAnalysisId":{"type":"string"},"siteName":{"type":"string"},"siteLocation":{"type":"string"}}}""";

    public string OutputSchemaJson =>
        """{"type":"object","required":["reported"],"properties":{"reported":{"type":"boolean"}}}""";

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
    {
        var root = input.RootElement;
        if (!root.TryGetProperty("siteAnalysisId", out var idElement) || !Guid.TryParse(idElement.GetString(), out var siteAnalysisId))
        {
            return AgentToolResult.Failure("A valid siteAnalysisId is required.");
        }

        try
        {
            var site = await LoadSitePointAsync(siteAnalysisId, cancellationToken);
            if (site is not { } resolved)
            {
                const string notFound = "The site analysis this finding belongs to could not be found.";
                await relay.ReportFailureAsync(siteAnalysisId, SiteAnalysisType.CharacterAnalysis, notFound, cause: null, cancellationToken);
                return AgentToolResult.Failure(notFound);
            }

            // FR-014 — exactly one call to the zoning seam; it returns Unavailable when no
            // real data source is configured, and the finding states that rather than estimating.
            var zoningOutcome = await zoningDataProvider.ResolveAsync(resolved.Latitude, resolved.Longitude, cancellationToken);

            var metadata = BuildMetadata(resolved.SiteName, zoningOutcome);
            using var content = ComposeContent(resolved.SiteName, zoningOutcome, metadata);

            await relay.ReportSuccessAsync(
                siteAnalysisId, SiteAnalysisType.CharacterAnalysis, metadata, content, documentId: null, cancellationToken);

            return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new { reported = true }));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A genuine fault — captured, persisted with its reason, and logged by the relay.
            await relay.ReportFailureAsync(siteAnalysisId, SiteAnalysisType.CharacterAnalysis, ex.Message, ex, cancellationToken);
            return AgentToolResult.Failure(ex.Message);
        }
    }

    /// <summary>Reads the frozen site point in its own scope (concurrent workflow branches share a DbContext).</summary>
    private async Task<(string SiteName, double Latitude, double Longitude)?> LoadSitePointAsync(
        Guid siteAnalysisId, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISiteAnalysisRepository>();
        var analysis = await repository.GetByIdAsync(siteAnalysisId, cancellationToken);

        return analysis is null
            ? null
            : (analysis.SiteName, analysis.Latitude, analysis.Longitude);
    }

    /// <summary>FR-011–FR-013 — each non-confirmed outcome is rendered distinctly; none collapses into another.
    /// An unavailable figure lowers the result rather than being left out of the calculation
    /// (FR-014, research.md D11).</summary>
    private static SiteAnalysisResultMetadata BuildMetadata(string siteName, DataResolutionOutcome<ZoningData> outcome)
    {
        var grounded = outcome.Type == DataResolutionOutcomeType.Confirmed && outcome.Value is { };
        var allGrounded = grounded; // single data source: either fully grounded or not
        var confidence = SiteAnalysisConfidence.ResolveConfidence(
            dataDirectlyDescribesSite: allGrounded,
            dataIsSparseOrApproximate: !allGrounded);

        var dataSource = grounded
            ? $"{outcome.Type}:{outcome.Value}"
            : "unavailable";

        string? reason = null;
        if (outcome.Type == DataResolutionOutcomeType.Unavailable && !string.IsNullOrWhiteSpace(outcome.Reason))
        {
            reason = outcome.Reason;
        }
        else if (outcome.Type == DataResolutionOutcomeType.Confirmed && outcome.Value is { } zoning)
        {
            // Build a concise data-source string from the zoning fields that are actually set.
            var parts = new List<string>();
            if (zooning.FloorAreaRatio.HasValue) parts.Add($"FAR={zooning.FloorAreaRatio.Value:N2}");
            if (zooning.SetbackMeters.HasValue) parts.Add($"setback={zooning.SetbackMeters.Value:N2} m");
            if (zooning.HeightLimitMeters.HasValue) parts.Add($"heightLimit={zooning.HeightLimitMeters.Value:N2} m");
            dataSource = parts.Count > 0 ? string.Join("; ", parts) : "unavailable";
        }

        var notes = new List<string>();
        if (!grounded && outcome.Type == DataResolutionOutcomeType.Unavailable)
        {
            notes.Add(NoZoningDataText);
        }

        return new SiteAnalysisResultMetadata(
            SiteAnalysisType.CharacterAnalysis, siteName, dataSource, confidence, DateTime.UtcNow, notes);
    }

    private JsonDocument ComposeContent(string siteName, DataResolutionOutcome<ZoningData> outcome, SiteAnalysisResultMetadata metadata)
    {
        var items = new List<SiteAnalysisContentItem>();

        if (outcome.Type == DataResolutionOutcomeType.Confirmed && outcome.Value is { } zoning)
        {
            if (zooning.FloorAreaRatio.HasValue)
                items.Add(new SiteAnalysisContentItem(FARText, MetresOrRatio(zooning.FloorAreaRatio.Value)));
            if (zooning.SetbackMeters.HasValue)
                items.Add(new SiteAnalysisContentItem(SetbackText, $"({zooning.SetbackMeters.Value:N2} m)"));
            if (zooning.HeightLimitMeters.HasValue)
                items.Add(new SiteAnalysisContentItem(HeightLimitText, $"({zooning.HeightLimitMeters.Value:N2} m)"));
        }
        else
        {
            items.Add(new SiteAnalysisContentItem("Zoning", NoZoningDataText));
        }

        var body = $"Zoning and urban-character data for {siteName}. Figures state their grounding; unavailable figures are explicit.";

        return composer.ComposeFinding(FindingHeading, body, image: null, metadata);
    }

    private static string MetresOrRatio(double value) => value >= 1.0
        ? $"{value:N2}"
        : $"{value:N4}";
}