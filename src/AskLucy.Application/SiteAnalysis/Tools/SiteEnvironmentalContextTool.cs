using System.Globalization;
using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.SiteAnalysis.Providers;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Common;
using AskLucy.Domain.SiteAnalysis;
using Microsoft.Extensions.DependencyInjection;
using SiteAnalysisAggregate = AskLucy.Domain.SiteAnalysis.SiteAnalysis;

namespace AskLucy.Application.SiteAnalysis.Tools;

/// <summary>
/// specs/088-environmental-context-specialist — the environmental-context specialist. It reports the
/// site's flood risk and climate/wind conditions from the two provider seams SPEC-057 declared, and a
/// reference to the platform's existing client-side Solar Analysis rather than any solar figure of
/// its own (052 research D3 — the backend performs no astronomy).
///
/// <para>
/// <b>No environmental data source exists.</b> Every figure is reported as unavailable with its
/// reason, and the finding says which dimensions this release does not assess at all. Nothing is
/// estimated, inferred, or read off imagery (FR-010–FR-015, FR-041/FR-042).
/// </para>
///
/// <para>
/// The site point comes from the persisted <see cref="SiteAnalysisAggregate"/>, in a fresh DI scope:
/// the providers take coordinates, the tool input carries names, and a concurrent workflow branch
/// shares the job's scoped <c>DbContext</c>, which is not safe to reuse even for reads
/// (research.md D1). The provider seams themselves are injected directly so a missing registration
/// fails at startup rather than at run time.
/// </para>
///
/// <para>
/// Reporting is exclusively through <see cref="ISiteAnalysisResultRelay"/>, inline as the last step
/// of <see cref="ExecuteAsync"/> — never through workflow node-completion events, which the parallel
/// executor batches (057 research D3).
/// </para>
/// </summary>
public sealed class SiteEnvironmentalContextTool(
    IServiceScopeFactory scopeFactory,
    SiteAnalysisContentComposer composer,
    ISiteAnalysisResultRelay relay,
    IFloodDataProvider floodDataProvider,
    IClimateDataProvider climateDataProvider) : IAgentTool
{
    public const string ToolName = "site_analysis_environmental_context";

    private const string NoDataSourceText = "No data source available";
    private const string NoCandidatesText = "No data available for this location";
    private const string AmbiguousText = "Conflicting data for this location";
    private const string NotReportedText = "Not reported by the source";
    private const string NotUsableText = "The source returned an unusable value";
    private const string NoSourceConfigured = "no data source configured";
    private const string ConfiguredSource = "configured environmental data source";

    private const string FindingHeading = "Environmental Context";
    private const string SunHeading = "Sun and shadow";
    private const string NotAssessedHeading = "Not assessed";

    /// <summary>
    /// FR-021–FR-025 — a pointer, not a measurement. The viewer's Solar Analysis acts on the site
    /// currently shown there, which the server cannot know, so the reference states what it covers
    /// instead of claiming those figures for the analysed site. It contains no solar figure and
    /// contributes nothing to confidence.
    /// </summary>
    private const string SunReferenceText =
        "Sun path, sunrise and sunset, day length, and real building shadows are computed live in the " +
        "browser by the viewer's Solar Analysis, for the site currently shown in the viewer. They are " +
        "not repeated here — open Solar Analysis from the viewer toolbar, or ask Lucy to show sun and shadows.";

    /// <summary>FR-041/FR-042 — the dimensions this release does not model at all, stated as out of scope rather than as missing values.</summary>
    private const string NotAssessedText =
        "This release does not assess temperature, humidity, precipitation, topography, slope, soil, " +
        "bedrock, water table, surface water, stormwater, ecology, protected status, noise, air quality, " +
        "or site hazards. Those dimensions are outside this specialist's current scope rather than " +
        "missing values.";

    public string Name => ToolName;

    public string Description =>
        "Reports flood risk and climate/wind conditions for a resolved site and reports them to the site analysis coordinator, together with a reference to the viewer's solar analysis.";

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
                await relay.ReportFailureAsync(siteAnalysisId, SiteAnalysisType.EnvironmentalContext, notFound, cause: null, cancellationToken);
                return AgentToolResult.Failure(notFound);
            }

            // FR-040 — exactly one call per configured seam, with the site point frozen at dispatch.
            var floodOutcome = await floodDataProvider.ResolveAsync(resolved.Latitude, resolved.Longitude, cancellationToken);
            var climateOutcome = await climateDataProvider.ResolveAsync(resolved.Latitude, resolved.Longitude, cancellationToken);

            var floodFigures = BuildFloodFigures(floodOutcome);
            var climateFigures = BuildClimateFigures(climateOutcome);
            var metadata = BuildMetadata(resolved.SiteName, [.. floodFigures, .. climateFigures]);

            using var content = ComposeContent(resolved.SiteName, floodFigures, climateFigures, metadata);

            await relay.ReportSuccessAsync(
                siteAnalysisId, SiteAnalysisType.EnvironmentalContext, metadata, content, documentId: null, cancellationToken);

            return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new { reported = true }));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // An exception is a fault, not an unavailable source (research.md D8). Captured,
            // persisted with its reason, and logged by the relay — never swallowed (§2 VIII).
            await relay.ReportFailureAsync(siteAnalysisId, SiteAnalysisType.EnvironmentalContext, ex.Message, ex, cancellationToken);
            return AgentToolResult.Failure(ex.Message);
        }
    }

    /// <summary>
    /// Reads the frozen site point in its own scope. The repository is scoped to a <c>DbContext</c>
    /// that concurrent workflow branches share, so it must never be resolved into the branch's scope
    /// (research.md D1).
    /// </summary>
    private async Task<(string SiteName, double Latitude, double Longitude)?> LoadSitePointAsync(
        Guid siteAnalysisId, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISiteAnalysisRepository>();
        var analysis = await repository.GetByIdAsync(siteAnalysisId, cancellationToken);

        return analysis is null ? null : (analysis.SiteName, analysis.Latitude, analysis.Longitude);
    }

    private static IReadOnlyList<Figure> BuildFloodFigures(DataResolutionOutcome<FloodData> outcome)
    {
        if (outcome.Type != DataResolutionOutcomeType.Confirmed || outcome.Value is not { } flood)
        {
            return NonConfirmedFigures(["Flood zone", "Annual exceedance probability"], outcome);
        }

        return
        [
            flood.FloodZoneClassification is { Length: > 0 } zone
                ? new Figure("Flood zone", zone, Grounded: true)
                : new Figure("Flood zone", NotReportedText, Grounded: false),

            flood.AnnualExceedanceProbabilityPercent is { } probability
                ? probability is >= 0 and <= 100
                    ? new Figure("Annual exceedance probability", probability.ToString("0.##", CultureInfo.InvariantCulture) + "%", Grounded: true)
                    : new Figure("Annual exceedance probability", NotUsableText, Grounded: false)
                : new Figure("Annual exceedance probability", NotReportedText, Grounded: false),
        ];
    }

    private static IReadOnlyList<Figure> BuildClimateFigures(DataResolutionOutcome<ClimateData> outcome)
    {
        if (outcome.Type != DataResolutionOutcomeType.Confirmed || outcome.Value is not { } climate)
        {
            return NonConfirmedFigures(["Prevailing wind direction", "Average wind speed"], outcome);
        }

        return
        [
            climate.PrevailingWindDirectionDegrees is { } direction
                ? direction is >= 0 and <= 360
                    ? new Figure("Prevailing wind direction", direction.ToString("0.#", CultureInfo.InvariantCulture) + "°", Grounded: true)
                    : new Figure("Prevailing wind direction", NotUsableText, Grounded: false)
                : new Figure("Prevailing wind direction", NotReportedText, Grounded: false),

            climate.AverageWindSpeedMetersPerSecond is { } speed
                ? speed >= 0
                    ? new Figure("Average wind speed", speed.ToString("0.#", CultureInfo.InvariantCulture) + " m/s", Grounded: true)
                    : new Figure("Average wind speed", NotUsableText, Grounded: false)
                : new Figure("Average wind speed", NotReportedText, Grounded: false),
        ];
    }

    /// <summary>FR-011–FR-013 — each non-confirmed outcome is rendered distinctly; none collapses into another.</summary>
    private static IReadOnlyList<Figure> NonConfirmedFigures<T>(IReadOnlyList<string> labels, DataResolutionOutcome<T> outcome)
    {
        var text = outcome.Type switch
        {
            DataResolutionOutcomeType.NoCandidates => NoCandidatesText,
            DataResolutionOutcomeType.Ambiguous => AmbiguousText,
            _ => NoDataSourceText,
        };

        var rendered = string.IsNullOrWhiteSpace(outcome.Reason) ? text : $"{text} — {outcome.Reason}";

        return [.. labels.Select(label => new Figure(label, rendered, Grounded: false))];
    }

    /// <summary>
    /// FR-031–FR-033 — coverage-driven confidence through the rule helper, from grounding facts
    /// only. An unavailable figure lowers the result rather than being left out of the calculation;
    /// today every figure is unavailable, so the finding reports the lowest level.
    /// </summary>
    private static SiteAnalysisResultMetadata BuildMetadata(string siteName, IReadOnlyList<Figure> figures)
    {
        var groundedCount = figures.Count(figure => figure.Grounded);
        var allGrounded = groundedCount == figures.Count;

        var confidence = SiteAnalysisConfidence.ResolveConfidence(
            dataDirectlyDescribesSite: allGrounded,
            dataIsSparseOrApproximate: groundedCount > 0 && !allGrounded);

        var dataSource = groundedCount > 0 ? ConfiguredSource : NoSourceConfigured;
        var notes = figures.Where(figure => !figure.Grounded).Select(figure => $"{figure.Label}: {figure.Value}").ToArray();

        return new SiteAnalysisResultMetadata(
            SiteAnalysisType.EnvironmentalContext, siteName, dataSource, confidence, DateTime.UtcNow, notes);
    }

    private JsonDocument ComposeContent(
        string siteName, IReadOnlyList<Figure> floodFigures, IReadOnlyList<Figure> climateFigures, SiteAnalysisResultMetadata metadata)
    {
        var sections = new List<SiteAnalysisContentSection>
        {
            new("Flood risk", ToItems(floodFigures)),
            new("Climate and wind", ToItems(climateFigures)),
            new(SunHeading, [], SunReferenceText),
            new(NotAssessedHeading, [], NotAssessedText),
        };

        return composer.ComposeSections(
            FindingHeading,
            $"Flood risk and climate conditions for {siteName}. Every figure states its own grounding.",
            sections,
            metadata);
    }

    private static IReadOnlyList<SiteAnalysisContentItem> ToItems(IReadOnlyList<Figure> figures) =>
        [.. figures.Select(figure => new SiteAnalysisContentItem(figure.Label, figure.Value))];

    private sealed record Figure(string Label, string Value, bool Grounded);
}
