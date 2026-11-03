using System.Globalization;
using System.Text;
using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.SiteAnalysis.Providers;
using AskLucy.Domain.Common;
using AskLucy.Domain.SiteAnalysis;
using AskLucy.Domain.SiteBoundaries;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SiteAnalysisAggregate = AskLucy.Domain.SiteAnalysis.SiteAnalysis;

namespace AskLucy.Application.SiteAnalysis.Tools;

internal static partial class SiteGeometryToolLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Site geometry for analysis {SiteAnalysisId}: the boundary snapshot could not be read; shape figures are unavailable")]
    public static partial void SnapshotUnreadable(ILogger logger, Guid siteAnalysisId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Site geometry for analysis {SiteAnalysisId}: the map source was unavailable ({Reason}); source-dependent figures are reported unavailable")]
    public static partial void SourceUnavailable(ILogger logger, Guid siteAnalysisId, string reason);
}

/// <summary>
/// specs/089-site-geometry-specialist — the geometry specialist. It measures the site's own shape
/// (area, dimensions, orientation, frontage, parcel outline) from the boundary frozen on the
/// analysis record plus one bounded OpenStreetMap query, and reports through
/// <see cref="ISiteAnalysisResultRelay"/> inline — never through workflow node events, which the
/// parallel executor batches (SPEC-057 research D3).
///
/// <para>
/// <b>A source outage is not a specialist failure.</b> Area, dimensions and orientation need no
/// network; when the map source cannot be reached they are still delivered, and the figures that
/// do depend on it (parcel outline, frontage) carry an explicit unavailable reason (spec US3). Only
/// a genuine fault — the analysis cannot be read, or an unexpected exception — fails the finding.
/// </para>
///
/// <para>
/// The site point and boundary come from the persisted <see cref="SiteAnalysisAggregate"/>, in a
/// fresh DI scope: a concurrent workflow branch shares the job's scoped <c>DbContext</c>, which is
/// not safe to reuse even for reads. Re-resolving the site by name is never done — the finding must
/// describe the site the analysis was started for.
/// </para>
/// </summary>
public sealed class SiteGeometryTool(
    IServiceScopeFactory scopeFactory,
    SiteAnalysisContentComposer composer,
    SiteGeometryCalculator calculator,
    ISiteGeometryDataProvider dataProvider,
    ISiteAnalysisResultRelay relay,
    ILogger<SiteGeometryTool> logger) : IAgentTool
{
    public const string ToolName = "site_analysis_site_geometry";

    private const string FindingHeading = "Site Geometry";
    private const string MeasurementsHeading = "Measurements";
    private const string FrontageHeading = "Frontage";
    private const string BoundaryHeading = "Boundary";
    private const string NoParcelRecorded = "Not recorded in OpenStreetMap";
    private const string SiteBoundaryBasis = "The site boundary resolved for this analysis";
    private const string SourceName = "OpenStreetMap (Overpass)";
    private const string SourceUnavailableName = "the analysis's resolved site boundary";
    private const string NoSourceName = "no geospatial source available";

    public string Name => ToolName;

    public string Description =>
        "Measures the analysed site's own geometry — parcel outline, area, dimensions, frontage and orientation — and reports it to the site analysis coordinator.";

    public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Low;

    public IReadOnlyList<AgentToolPermission> RequiredPermissions => [AgentToolPermission.ExternalNetwork];

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
            var site = await LoadSiteAsync(siteAnalysisId, cancellationToken);
            if (site is not { } resolved)
            {
                const string notFound = "The site analysis this finding belongs to could not be found.";
                await relay.ReportFailureAsync(siteAnalysisId, SiteAnalysisType.SiteGeometry, notFound, cause: null, cancellationToken);
                return AgentToolResult.Failure(notFound);
            }

            var snapshot = SiteBoundarySnapshot.TryParse(resolved.BoundaryGeoJson);
            if (snapshot.UnavailableReason == SiteBoundarySnapshot.UnreadableReason)
            {
                // A data-integrity problem, not "the site has no shape" — logged so an operator can
                // find it, and stated in the finding so the user is not misled.
                SiteGeometryToolLog.SnapshotUnreadable(logger, siteAnalysisId);
            }

            var dataOutcome = await dataProvider.ResolveAsync(
                new GeoPoint(resolved.Latitude, resolved.Longitude), snapshot.Ring, cancellationToken);

            var sourceReason = dataOutcome.Type == DataResolutionOutcomeType.Confirmed
                ? null
                : dataOutcome.Reason ?? "The map source is unavailable right now.";

            if (sourceReason is not null)
            {
                SiteGeometryToolLog.SourceUnavailable(logger, siteAnalysisId, sourceReason);
            }

            var data = dataOutcome.Value ?? SiteGeometryFeatureData.Empty;
            var sitePoint = new GeoPoint(resolved.Latitude, resolved.Longitude);

            var parcel = snapshot.Ring is null ? null : calculator.FindParcel(data.Parcels, sitePoint);
            var measurements = snapshot.Ring is null ? null : calculator.Measure(snapshot.Ring, data.Streets);

            var metadata = BuildMetadata(resolved.SiteName, parcel, measurements, snapshot, sourceReason);
            using var content = ComposeContent(resolved.SiteName, parcel, measurements, snapshot, sourceReason, metadata);

            await relay.ReportSuccessAsync(
                siteAnalysisId, SiteAnalysisType.SiteGeometry, metadata, content, documentId: null, cancellationToken);

            return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new { reported = true }));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A genuine fault — captured, persisted with its reason, and logged by the relay.
            await relay.ReportFailureAsync(siteAnalysisId, SiteAnalysisType.SiteGeometry, ex.Message, ex, cancellationToken);
            return AgentToolResult.Failure(ex.Message);
        }
    }

    /// <summary>Reads the frozen site point and boundary in its own scope (concurrent branches share a DbContext).</summary>
    private async Task<(string SiteName, double Latitude, double Longitude, string? BoundaryGeoJson)?> LoadSiteAsync(
        Guid siteAnalysisId, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISiteAnalysisRepository>();
        var analysis = await repository.GetByIdAsync(siteAnalysisId, cancellationToken);

        return analysis is null
            ? null
            : (analysis.SiteName, analysis.Latitude, analysis.Longitude, analysis.BoundaryGeoJson);
    }

    /// <summary>
    /// FR-023/FR-024 — rule-based confidence from grounding facts only, never an importance score.
    /// A recorded parcel describes the site directly; the resolved site boundary is a real but
    /// approximate basis; no shape at all means nothing is measured.
    /// </summary>
    private static SiteAnalysisResultMetadata BuildMetadata(
        string siteName,
        SiteGeometryParcel? parcel,
        SiteGeometryMeasurements? measurements,
        SiteBoundarySnapshotResult snapshot,
        string? sourceReason)
    {
        var confidence = SiteAnalysisConfidence.ResolveConfidence(
            dataDirectlyDescribesSite: parcel is not null,
            dataIsSparseOrApproximate: parcel is null && snapshot.HasRing);

        var dataSource = sourceReason is null
            ? SourceName
            : snapshot.HasRing ? SourceUnavailableName : NoSourceName;

        var notes = new List<string>();

        if (snapshot.UnavailableReason is { } boundaryReason)
        {
            notes.Add($"Boundary: {boundaryReason}");
        }

        if (sourceReason is { } unavailable)
        {
            notes.Add($"Parcel boundary: {unavailable}");
            notes.Add($"Frontage: {unavailable}");
        }
        else
        {
            if (parcel is null && snapshot.HasRing)
            {
                notes.Add("Parcel boundary: none recorded in OpenStreetMap for this site.");
            }

            if (measurements is { Frontages.Count: 0 })
            {
                notes.Add($"Frontage: no named street within {SiteGeometryCalculator.FrontageToleranceMeters.ToString("0", CultureInfo.InvariantCulture)} m of the site boundary.");
            }
        }

        return new SiteAnalysisResultMetadata(
            SiteAnalysisType.SiteGeometry, siteName, dataSource, confidence, DateTime.UtcNow, notes);
    }

    private JsonDocument ComposeContent(
        string siteName,
        SiteGeometryParcel? parcel,
        SiteGeometryMeasurements? measurements,
        SiteBoundarySnapshotResult snapshot,
        string? sourceReason,
        SiteAnalysisResultMetadata metadata)
    {
        var sections = new List<SiteAnalysisContentSection>
        {
            measurements is null
                ? new SiteAnalysisContentSection(MeasurementsHeading, [], snapshot.UnavailableReason)
                : new SiteAnalysisContentSection(MeasurementsHeading, MeasurementItems(measurements)),
            BuildFrontageSection(measurements, snapshot, sourceReason),
            BuildBoundarySection(parcel, measurements, snapshot, sourceReason),
        };

        var body =
            $"Measured geometry for {siteName}. Figures are measured from the site boundary " +
            "resolved for this analysis unless a recorded parcel outline exists.";

        return composer.ComposeSections(FindingHeading, body, sections, metadata);
    }

    private static SiteAnalysisContentSection BuildFrontageSection(
        SiteGeometryMeasurements? measurements, SiteBoundarySnapshotResult snapshot, string? sourceReason)
    {
        if (snapshot.Ring is null)
        {
            return new SiteAnalysisContentSection(FrontageHeading, [], snapshot.UnavailableReason);
        }

        if (sourceReason is not null)
        {
            return new SiteAnalysisContentSection(FrontageHeading, [], $"Unavailable — {sourceReason}");
        }

        if (measurements is not { Frontages.Count: > 0 })
        {
            return new SiteAnalysisContentSection(FrontageHeading, [],
                $"No named street runs within {SiteGeometryCalculator.FrontageToleranceMeters:0} m of the site boundary in the map data.");
        }

        var items = measurements.Frontages
            .Select(frontage => new SiteAnalysisContentItem(frontage.StreetName, Metres(frontage.LengthMeters)))
            .Append(new SiteAnalysisContentItem("Total frontage", Metres(measurements.TotalFrontageMeters)))
            .ToList();

        return new SiteAnalysisContentSection(FrontageHeading, items);
    }

    private static SiteAnalysisContentSection BuildBoundarySection(
        SiteGeometryParcel? parcel, SiteGeometryMeasurements? measurements, SiteBoundarySnapshotResult snapshot, string? sourceReason)
    {
        if (snapshot.Ring is null)
        {
            return new SiteAnalysisContentSection(BoundaryHeading, [], snapshot.UnavailableReason);
        }

        var items = new List<SiteAnalysisContentItem>
        {
            new("Parcel boundary", sourceReason is not null
                ? $"Unavailable — {sourceReason}"
                : parcel is null ? NoParcelRecorded : DescribeParcel(parcel)),
            new("Measurement basis", SiteBoundaryBasis),
        };

        var body = new StringBuilder();
        if (sourceReason is null && parcel is null)
        {
            body.Append("No parcel or plot boundary is recorded in OpenStreetMap for this site, so these figures describe the site boundary resolved for this analysis. ");
        }

        if (measurements?.LooksLikeResolvedBuffer == true)
        {
            body.Append("That boundary is an approximate buffer around the site's point rather than a surveyed outline, so the figures describe that buffer. ");
        }

        return new SiteAnalysisContentSection(
            BoundaryHeading, items, body.Length == 0 ? null : body.ToString().TrimEnd());
    }

    private static string DescribeParcel(SiteGeometryParcel parcel)
    {
        var kind = parcel.Tags.TryGetValue("boundary", out var boundary) ? boundary : "outline";
        return $"Recorded in OpenStreetMap (boundary={kind})";
    }

    private static IReadOnlyList<SiteAnalysisContentItem> MeasurementItems(SiteGeometryMeasurements measurements) =>
    [
        new("Area", Area(measurements.AreaSquareMeters)),
        new("Dimensions", $"{Metres(measurements.LengthMeters)} × {Metres(measurements.WidthMeters)}"),
        new("Orientation", Orientation(measurements.OrientationBearingDegrees)),
    ];

    private static string Area(double squareMetres) =>
        squareMetres >= 10_000
            ? $"{squareMetres.ToString("N0", CultureInfo.InvariantCulture)} m² ({squareMetres / 10_000:N2} ha)"
            : $"{squareMetres.ToString("N0", CultureInfo.InvariantCulture)} m²";

    private static string Metres(double metres) =>
        $"{metres.ToString("N0", CultureInfo.InvariantCulture)} m";

    /// <summary>FR-014 — a compass direction the user can act on, with the bearing kept checkable.</summary>
    private static string Orientation(double? bearingDegrees)
    {
        if (bearingDegrees is not { } bearing)
        {
            return "Not meaningful — the site's two extents are nearly equal";
        }

        var direction = bearing switch
        {
            < 22.5 or >= 157.5 => "north–south",
            < 67.5 => "north-east–south-west",
            < 112.5 => "east–west",
            _ => "north-west–south-east",
        };

        return $"{direction} ({bearing.ToString("N0", CultureInfo.InvariantCulture)}°)";
    }
}
