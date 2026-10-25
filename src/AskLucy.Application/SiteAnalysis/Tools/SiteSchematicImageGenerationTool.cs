using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Documents.Commands;
using AskLucy.Application.MapCaptures;
using AskLucy.Application.MapCaptures.Queries.CaptureMap;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Common;
using AskLucy.Domain.SiteAnalysis;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SiteAnalysisAggregate = AskLucy.Domain.SiteAnalysis.SiteAnalysis;

namespace AskLucy.Application.SiteAnalysis.Tools;

/// <summary>
/// contracts/site-map-capture-tool.md — the site-analysis specialist that shows the platform's
/// <b>real map capture</b> of the analysed site (specs/090-real-map-capture). The image comes from
/// the capture built in specs/083, reached in-process through <see cref="CaptureMapQuery"/> — never
/// the HTTP endpoint, its service-key authentication or its rate-limit bucket — and is centred on
/// the coordinates frozen onto the <see cref="SiteAnalysisAggregate"/> at dispatch. No image model
/// is involved: capture replaces generation outright, and there is no generated fallback.
///
/// <para>
/// The returned bytes are persisted as a platform Document through the existing
/// <see cref="DocumentUploadFinalizer"/> — the capture itself persists nothing (083 FR-014) — and
/// only the document id reaches the finding's image block. Reporting is exclusively through
/// <see cref="ISiteAnalysisResultRelay"/>, inline as the last step of <see cref="ExecuteAsync"/>
/// (057 tasks rule 1). Every failure is recorded there with the capture's own reason vocabulary and
/// delivers nothing, per 057 FR-024's deliberate user silence.
/// </para>
///
/// <para>
/// The class name mirrors the stable tool name (<c>site_analysis_schematic_image</c>) that the
/// published system workflow, the provisioner and the DI registration all reference; it is
/// deliberately not renamed. The user-visible label is "Site Map" — see the content below.
/// </para>
/// </summary>
public sealed class SiteSchematicImageGenerationTool(
    IServiceScopeFactory scopeFactory,
    SiteAnalysisContentComposer composer,
    ISiteAnalysisResultRelay relay) : IAgentTool
{
    public const string ToolName = "site_analysis_schematic_image";

    private const string Heading = "Site Map";

    /// <summary>SPEC-083's default for an unnamed request, and the only mode the default (Esri) source serves.</summary>
    private const string CaptureMode = "satellite";

    private const string DocumentActor = "system:site-analysis-map-capture";

    public string Name => ToolName;

    public string Description =>
        "Captures the platform's real map of the analysed site and reports it to the site analysis coordinator.";

    public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Low;

    public IReadOnlyList<AgentToolPermission> RequiredPermissions =>
        [AgentToolPermission.ExternalNetwork, AgentToolPermission.WriteFile];

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
            // Everything that touches the database — the analysis read, the capture's settings
            // read and the document write — runs in this isolated scope: concurrent workflow
            // branches share the job's scoped DbContext, which is not safe for concurrent use even
            // for reads (057 research D2, mirroring SiteEnvironmentalContextTool).
            using var scope = scopeFactory.CreateScope();

            var repository = scope.ServiceProvider.GetRequiredService<ISiteAnalysisRepository>();
            var site = await repository.GetByIdAsync(siteAnalysisId, cancellationToken);
            if (site is null)
            {
                const string notFound = "The site analysis this finding belongs to could not be found.";
                await relay.ReportFailureAsync(siteAnalysisId, SiteAnalysisType.SchematicImage, notFound, cause: null, cancellationToken);
                return AgentToolResult.Failure(notFound);
            }

            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var capture = await sender.Send(
                new CaptureMapQuery(
                    // The point frozen at dispatch. Never the site name: a name costs an extra
                    // geocoding call and can resolve a different place from the one analysed.
                    Location: null,
                    Latitude: site.Latitude,
                    Longitude: site.Longitude,
                    // The capture's own configured neighbourhood view, source, crop and zoom are
                    // administrator-controlled (083) and are deliberately not named here.
                    ViewSizeMetres: null,
                    Mode: CaptureMode,
                    Overlays: null,
                    CallerName: $"site-analysis:{siteAnalysisId}",
                    Source: null,
                    CropPixels: null,
                    Zoom: null),
                cancellationToken);

            if (!capture.IsSuccess)
            {
                // FR-018/FR-019 — the capture's own reason vocabulary, recorded rather than shown.
                var failure = $"{capture.FailureReason}: {capture.FailureDetail}";
                await relay.ReportFailureAsync(siteAnalysisId, SiteAnalysisType.SchematicImage, failure, cause: null, cancellationToken);
                return AgentToolResult.Failure(failure);
            }

            if (capture.Png is not { } png)
            {
                // A success with no bytes violates the capture's contract; it is still a failure,
                // never an empty finding (FR-020).
                const string unusable = "The capture reported success but returned no image bytes.";
                await relay.ReportFailureAsync(siteAnalysisId, SiteAnalysisType.SchematicImage, unusable, cause: null, cancellationToken);
                return AgentToolResult.Failure(unusable);
            }

            if (capture.Source is not { } source)
            {
                // Provenance must name the source that actually served the pixels (FR-015); a
                // guessed source would be worse than no finding.
                const string noSource = "The capture did not report which imagery source served the image.";
                await relay.ReportFailureAsync(siteAnalysisId, SiteAnalysisType.SchematicImage, noSource, cause: null, cancellationToken);
                return AgentToolResult.Failure(noSource);
            }

            var documentId = await PersistDocumentAsync(scope, site.UserId, png, cancellationToken);

            // FR-024 — the frame is centred on the exact coordinates frozen at dispatch, with no
            // geocoding and no candidate selection, and the pixels are a real record of the place:
            // nothing was interpolated, substituted from a nearby point, or generated. The imagery
            // is undated, which limits the temporal claim the body states — not whether the image
            // directly describes the site. GeocodingCandidate.Importance is never consulted.
            var confidence = SiteAnalysisConfidence.ResolveConfidence(
                dataDirectlyDescribesSite: true,
                dataIsSparseOrApproximate: false);

            var metadata = new SiteAnalysisResultMetadata(
                SiteAnalysisType.SchematicImage,
                site.SiteName,
                $"map-capture:{MapCaptureSourceWire.WireValueOf(source)}",
                confidence,
                DateTime.UtcNow,
                []);

            using var content = composer.ComposeFinding(
                Heading,
                DescribeCapture(site.SiteName),
                (documentId, AltText(site.SiteName)),
                metadata);

            await relay.ReportSuccessAsync(siteAnalysisId, SiteAnalysisType.SchematicImage, metadata, content, documentId, cancellationToken);
            return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new { reported = true }));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Any other failure — the record, the document write, an unexpected fault — is
            // captured and reported with its cause (FR-021), never swallowed (constitution §2 VIII).
            await relay.ReportFailureAsync(siteAnalysisId, SiteAnalysisType.SchematicImage, ex.Message, ex, cancellationToken);
            return AgentToolResult.Failure(ex.Message);
        }
    }

    /// <summary>
    /// Stores the capture PNG as a platform Document in the same isolated scope. The finalizer
    /// performs magic-byte validation, storage-quota enforcement, checksum de-duplication and the
    /// Document/version/checksum creation; the unit of work commits them (057 research D8).
    /// </summary>
    private static async Task<Guid> PersistDocumentAsync(
        IServiceScope scope, string ownerId, byte[] png, CancellationToken cancellationToken)
    {
        var finalizer = scope.ServiceProvider.GetRequiredService<DocumentUploadFinalizer>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        using var content = new MemoryStream(png, writable: false);

        // The extension must be exactly ".png": the upload validator sniffs the magic bytes and
        // rejects a PNG whose name says otherwise.
        var fileName = $"site-analysis-site-map-{Guid.CreateVersion7()}.png";

        var result = await finalizer.FinalizeAsync(
            ownerId, fileName, content, content.Length, DocumentActor, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var documentId = result.IsDuplicate ? result.DuplicateOfDocumentId : result.Document?.Id;
        return documentId
            ?? throw new InvalidOperationException("The captured image could not be stored as a document.");
    }

    /// <summary>
    /// FR-012 — the finding says what the image is and is not. The imagery is the provider's most
    /// recent available capture: it is not live and carries no capture date, and the wording must
    /// never imply otherwise. This mirrors the platform's guidance for the map-snapshot capability.
    /// </summary>
    private static string DescribeCapture(string siteName) =>
        $"The platform's map capture of {siteName}, centred on the site's coordinates. The imagery is " +
        "the provider's most recent available capture of this area; it is not a live view, and it " +
        "carries no capture date.";

    private static string AltText(string siteName) =>
        $"Satellite map centred on {siteName}, captured by the platform";
}
