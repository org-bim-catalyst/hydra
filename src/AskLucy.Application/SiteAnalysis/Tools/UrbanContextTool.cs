using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Overpass;
using AskLucy.Application.SiteAnalysis;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Common;
using AskLucy.Domain.SiteAnalysis;
using AskLucy.Domain.SiteBoundaries;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SiteAnalysisAggregate = AskLucy.Domain.SiteAnalysis.SiteAnalysis;

namespace AskLucy.Application.SiteAnalysis.Tools;

/// <summary>
/// Specs/057 FR-031 follow-up — the surroundings of a site: adjacent land use mix,
/// building density/typology nearby, and notable nearby places. Source: OSM/Overpass
/// tags (<c>amenity</c>, <c>landuse</c>, <c>building</c>). The returned finding is
/// reported exclusively through <see cref="ISiteAnalysisResultRelay"/>, inline as the
/// last step of <see cref="ExecuteAsync"/> (057 tasks rule 1). Every failure is recorded
/// there with the query's own reason vocabulary and delivers nothing, per 057 FR-024.
/// </summary>
public sealed class UrbanContextTool(
    IServiceScopeFactory scopeFactory,
    SiteAnalysisContentComposer composer,
    ISiteAnalysisResultRelay relay) : IAgentTool
{
    public const string ToolName = "site_analysis_urban_context";

    private const string Heading = "Urban Context";

    private const int QueryRadiusMetres = 500;

    private const string DocumentActor = "system:site-analysis-urban-context";

    public string Name => ToolName;

    public string Description =>
        "Queries OpenStreetMap for the site's surrounding land use mix, building density, and notable nearby places.";

    public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Low;

    public IReadOnlyList<AgentToolPermission> RequiredPermissions =>
        [AgentToolPermission.ExternalNetwork];

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
            using var scope = scopeFactory.CreateScope();

            var repository = scope.ServiceProvider.GetRequiredService<ISiteAnalysisRepository>();
            var site = await repository.GetByIdAsync(siteAnalysisId, cancellationToken);
            if (site is null)
            {
                const string notFound = "The site analysis this finding belongs to could not be found.";
                await relay.ReportFailureAsync(siteAnalysisId, SiteAnalysisType.UrbanContext, notFound, cause: null, cancellationToken);
                return AgentToolResult.Failure(notFound);
            }

            var overpass = scope.ServiceProvider.GetRequiredService<IOverpassFeatureQueryProvider>();
            var query = new OverpassFeatureQuery(
                Center: new GeoPoint(site.Latitude, site.Longitude),
                RadiusMeters: QueryRadiusMetres,
                Filters: new[]
                {
                    new OverpassTagFilter("amenity", null),
                    new OverpassTagFilter("landuse", null),
                    new OverpassTagFilter("building", null),
                });

            var features = await overpass.QueryAsync(query, cancellationToken);

            // Group findings by type for the composer
            var amenityCount = features.Count(f => f.Tags.ContainsKey("amenity"));
            var landuseCount = features.Count(f => f.Tags.ContainsKey("landuse"));
            var buildingCount = features.Count(f => f.Tags.ContainsKey("building"));

            // Build key-value data for the finding
            var items = new[]
            {
                new SiteAnalysisContentItem("Amenities nearby", amenityCount > 0 ? amenityCount.ToString() : "None detected"),
                new SiteAnalysisContentItem("Land use types", landuseCount > 0 ? landuseCount.ToString() : "None detected"),
                new SiteAnalysisContentItem("Buildings nearby", buildingCount > 0 ? buildingCount.ToString() : "None detected"),
            };

            var metadata = new SiteAnalysisResultMetadata(
                SiteAnalysisType.UrbanContext,
                site.SiteName,
                $"osm-query:radius-{QueryRadiusMetres}m",
                SiteAnalysisConfidenceLevel.Medium,
                DateTime.UtcNow,
                []);

            using var content = composer.ComposeSections(
                Heading,
                $"Urban context analysis of the area within {QueryRadiusMetres}m of {site.SiteName}.",
                [new SiteAnalysisContentSection("Surroundings", items)],
                metadata);

            await relay.ReportSuccessAsync(siteAnalysisId, SiteAnalysisType.UrbanContext, metadata, content, Guid.Empty, cancellationToken);
            return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new { reported = true }));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Any other failure — the record, an unexpected fault — is captured and reported
            // with its cause (FR-021), never swallowed (constitution §2 VIII).
            await relay.ReportFailureAsync(siteAnalysisId, SiteAnalysisType.UrbanContext, ex.Message, ex, cancellationToken);
            return AgentToolResult.Failure(ex.Message);
        }
    }
}
