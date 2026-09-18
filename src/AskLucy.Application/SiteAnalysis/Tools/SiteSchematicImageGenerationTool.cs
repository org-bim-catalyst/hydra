using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Images;
using AskLucy.Application.Documents.Commands;
using AskLucy.Domain.Agents;
using AskLucy.Domain.SiteAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace AskLucy.Application.SiteAnalysis.Tools;

/// <summary>
/// contracts/schematic-image-prompt.md — the one specialist shipped in this release. Generates a
/// top-down schematic site map through <see cref="IImageGenerationService"/> — i.e. whichever
/// provider and image model the administrator assigned to the <c>ImageGeneration</c> capability,
/// never a model of its own — persists it as a platform Document (FR-029), and reports the result inline to
/// <see cref="ISiteAnalysisResultRelay"/> (tasks.md rule 1 — never via workflow node events).
/// Invoked by a <c>NativeTool</c> workflow node, never <c>AiAgent</c> (research.md D2).
/// </summary>
public sealed class SiteSchematicImageGenerationTool(
    IServiceScopeFactory scopeFactory,
    SiteAnalysisContentComposer composer,
    ISiteAnalysisResultRelay relay) : IAgentTool
{
    public string Name => "site_analysis_schematic_image";

    public string Description => "Generates a top-down schematic site analysis map for a resolved site and reports it to the site analysis coordinator.";

    public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Low;

    public IReadOnlyList<AgentToolPermission> RequiredPermissions => [AgentToolPermission.ExternalNetwork, AgentToolPermission.WriteFile];

    public string InputSchemaJson =>
        """{"type":"object","required":["siteAnalysisId","siteName","siteLocation"],"properties":{"siteAnalysisId":{"type":"string"},"siteName":{"type":"string"},"siteLocation":{"type":"string"}}}""";

    public string OutputSchemaJson =>
        """{"type":"object","required":["reported"],"properties":{"reported":{"type":"boolean"}}}""";

    public async Task<AgentToolResult> ExecuteAsync(AgentToolExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
    {
        var root = input.RootElement;
        if (!root.TryGetProperty("siteAnalysisId", out var idElement) || !Guid.TryParse(idElement.GetString(), out var siteAnalysisId))
        {
            return AgentToolResult.Failure("A valid siteAnalysisId is required.");
        }

        var siteName = root.TryGetProperty("siteName", out var nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty;
        var siteLocation = root.TryGetProperty("siteLocation", out var locationElement) ? locationElement.GetString() ?? string.Empty : string.Empty;

        try
        {
            var (documentId, dataSource) = await GenerateAndPersistImageAsync(context.UserId, siteName, siteLocation, cancellationToken);

            // contracts/schematic-image-prompt.md § Provenance reported — Medium: the site and its
            // coordinates are exact (not sparse or nearby-only), but a generated image is a
            // generative interpretation rather than a direct, measured description of the site, so
            // it does not qualify as High under SiteAnalysisConfidence's rule.
            var confidence = SiteAnalysisConfidence.ResolveConfidence(dataDirectlyDescribesSite: false, dataIsSparseOrApproximate: true);
            var metadata = new SiteAnalysisResultMetadata(
                SiteAnalysisType.SchematicImage, siteName, dataSource,
                confidence, DateTime.UtcNow, []);

            using var content = composer.ComposeFinding(
                "Schematic Site Map",
                $"A generated top-down schematic map of {siteName}, showing streets, buildings, green space and water bodies with a legend.",
                (documentId, $"Schematic top-down site analysis map of {siteName}"),
                metadata);

            await relay.ReportSuccessAsync(siteAnalysisId, SiteAnalysisType.SchematicImage, metadata, content, documentId, cancellationToken);
            return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new { reported = true }));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is NotSupportedException or AiProviderException or AiCapabilityNotConfiguredException or InvalidGeneratedImageException)
        {
            // FR-030 — no image model is assigned, the provider cannot generate images, it
            // returned something unusable, or the call otherwise failed in a classified way. Reuse the existing exception hierarchy; never invent a new one.
            // Reported to the relay (captured, persisted, logged — FR-022) AND returned as an
            // AgentToolResult.Failure, so the workflow's own node/audit trail also reflects the
            // failure and the AnyCompleted merge strategy's tolerance is genuinely exercised
            // (tasks.md rule 5), not masked by an artificially "successful" node.
            await relay.ReportFailureAsync(siteAnalysisId, SiteAnalysisType.SchematicImage, ex.Message, ex, cancellationToken);
            return AgentToolResult.Failure(ex.Message);
        }
        catch (Exception ex)
        {
            // Any other failure (download, storage quota, validation) — still captured and
            // reported through the relay (FR-022), never silently discarded (constitution §2 VIII).
            await relay.ReportFailureAsync(siteAnalysisId, SiteAnalysisType.SchematicImage, ex.Message, ex, cancellationToken);
            return AgentToolResult.Failure(ex.Message);
        }
    }

    private async Task<(Guid DocumentId, string DataSource)> GenerateAndPersistImageAsync(
        string ownerId, string siteName, string siteLocation, CancellationToken cancellationToken)
    {
        var prompt = SiteAnalysisPrompts.BuildSchematicImagePrompt(siteName, siteLocation);

        // Everything that touches the database — the ImageGeneration capability lookup inside
        // IImageGenerationService as well as the document write — runs in this isolated scope:
        // the tool may execute inside a concurrent workflow branch that shares the job's own
        // scoped DbContext, and a DbContext is not safe for concurrent use even for reads
        // (research.md D2, mirroring ScopeIsolatedSiteAnalysisResultRelay).
        using var scope = scopeFactory.CreateScope();
        var imageGeneration = scope.ServiceProvider.GetRequiredService<IImageGenerationService>();
        var finalizer = scope.ServiceProvider.GetRequiredService<DocumentUploadFinalizer>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var generated = await imageGeneration.GenerateAsync(prompt, cancellationToken);

        using var content = new MemoryStream(generated.Image.Content, writable: false);
        var fileName = $"site-analysis-schematic-{Guid.CreateVersion7()}{generated.Image.FileExtension}";
        var result = await finalizer.FinalizeAsync(ownerId, fileName, content, content.Length, "system:site-analysis-schematic-image", cancellationToken);
        var documentId = result.IsDuplicate ? result.DuplicateOfDocumentId!.Value : result.Document!.Id;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return (documentId, $"{generated.ProviderName}:{generated.ModelKey}");
    }
}
