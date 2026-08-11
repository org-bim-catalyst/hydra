using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Mcp.Resilience;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Mcp;
using MediatR;

namespace AskLucy.Application.Mcp.Commands.RefreshMcpCapabilities;

/// <summary>
/// FR-011 discovery pipeline: connect → discover → normalize → store → mark
/// added/removed/changed (FR-015) → the prior successful snapshot's rows are left untouched on
/// failure (FR-016). Tools always default to <see cref="AgentToolRiskLevel.Critical"/> and
/// empty <c>RequiredPermissionsJson</c> on discovery — MCP's own protocol carries no risk/
/// permission metadata to parse, and FR-022 treats any server-declared hint as advisory only
/// regardless, so skipping that parsing changes nothing about the mandatory admin review gate.
/// </summary>
public sealed class RefreshMcpCapabilitiesCommandHandler(
    IMcpServerRepository serverRepository,
    IMcpToolRepository toolRepository,
    IMcpResourceRepository resourceRepository,
    IMcpPromptRepository promptRepository,
    IMcpAuditLogRepository auditLogRepository,
    IMcpClientFactory clientFactory,
    McpConnectionResiliencePolicy resiliencePolicy,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<RefreshMcpCapabilitiesCommand, McpCapabilityRefreshResultDto>
{
    public async Task<McpCapabilityRefreshResultDto> Handle(RefreshMcpCapabilitiesCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var server = await serverRepository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"MCP server {request.Id} was not found.");

        auditLogRepository.Add(McpAuditLog.Record(server.Id, userId, McpAuditAction.CapabilityDiscoveryStarted, null, "{}"));

        var nextVersion = await serverRepository.GetLatestCapabilitySnapshotVersionAsync(server.Id, cancellationToken) + 1;

        IReadOnlyList<McpDiscoveredTool> discoveredTools;
        try
        {
            discoveredTools = await resiliencePolicy.ExecuteAsync(server.Id, isIdempotent: true, async ct =>
            {
                var client = await clientFactory.GetOrCreateAsync(server.Id, ct);
                return await client.ListToolsAsync(ct);
            }, cancellationToken);
        }
        catch (Exception)
        {
            var failureCategory = McpFailureCategory.CapabilityDiscoveryFailure;
            var failedSnapshot = McpCapabilitySnapshot.CreateFailed(server.Id, nextVersion, failureCategory, userId);
            serverRepository.AddCapabilitySnapshot(failedSnapshot);
            auditLogRepository.Add(McpAuditLog.Record(server.Id, userId, McpAuditAction.CapabilityDiscoveryFailed, failureCategory, "{}"));
            await unitOfWork.SaveChangesAsync(cancellationToken);

            // FR-016 — the prior successful snapshot's tools/resources/prompts are never touched here.
            return new McpCapabilityRefreshResultDto(false, null, 0, 0, 0);
        }

        var client = await clientFactory.GetOrCreateAsync(server.Id, cancellationToken);
        var discoveredResources = await TryListAsync(() => client.ListResourcesAsync(cancellationToken));
        var discoveredPrompts = await TryListAsync(() => client.ListPromptsAsync(cancellationToken));

        var declaredCapabilities = new List<string> { "Tools" };
        if (discoveredResources is not null)
        {
            declaredCapabilities.Add("Resources");
        }

        if (discoveredPrompts is not null)
        {
            declaredCapabilities.Add("Prompts");
        }

        var snapshot = McpCapabilitySnapshot.CreateSuccessful(server.Id, nextVersion, JsonSerializer.Serialize(declaredCapabilities), null, userId);
        serverRepository.AddCapabilitySnapshot(snapshot);

        var added = 0;
        var changed = 0;
        foreach (var discoveredTool in discoveredTools)
        {
            var prior = await toolRepository.GetLatestByServerAndToolNameAsync(server.Id, discoveredTool.Name, cancellationToken);
            var inputSchemaJson = discoveredTool.InputSchema.GetRawText();
            var outputSchemaJson = discoveredTool.OutputSchema?.GetRawText() ?? "{}";

            (McpToolActivationStatus Status, string? ActivatedByUserId, DateTime? ActivatedAtUtc)? carriedForward = null;
            if (prior is null)
            {
                added++;
            }
            else if (prior.InputSchemaJson == inputSchemaJson && prior.OutputSchemaJson == outputSchemaJson && prior.Description == (discoveredTool.Description ?? string.Empty))
            {
                // Unchanged since the last snapshot — carry the administrator's prior review
                // forward rather than requiring re-review on every routine refresh
                // (contracts/mcp-lifecycle-events.md's re-review rule).
                carriedForward = (prior.ActivationStatus, prior.ActivatedByUserId, prior.ActivatedAtUtc);
            }
            else
            {
                changed++;
            }

            var tool = McpTool.CreateFromDiscovery(
                server.Id, snapshot.Id, discoveredTool.Name, discoveredTool.Title ?? discoveredTool.Name, discoveredTool.Description ?? string.Empty,
                inputSchemaJson, outputSchemaJson, null, null, "[]", null, carriedForward);
            toolRepository.Add(tool);
        }

        var removedToolCount = await MarkRemovedAsync(
            server.Id, snapshot.Id, discoveredTools.Select(t => t.Name).ToHashSet(StringComparer.Ordinal), toolRepository, cancellationToken);

        foreach (var discoveredResource in discoveredResources ?? [])
        {
            var resource = McpResource.CreateFromDiscovery(server.Id, snapshot.Id, discoveredResource.Uri, discoveredResource.Name ?? discoveredResource.Uri, discoveredResource.Description, discoveredResource.MimeType);
            resourceRepository.Add(resource);
        }

        foreach (var discoveredPrompt in discoveredPrompts ?? [])
        {
            var content = await client.GetPromptAsync(discoveredPrompt.Name, null, cancellationToken);
            var existingPrompt = await promptRepository.GetByNamespacedNameAsync($"mcp:{server.Id}:{discoveredPrompt.Name}", cancellationToken);
            if (existingPrompt is not null)
            {
                // Clarification — a prompt is a read-only mirror, mutated in place rather than
                // creating a new row per snapshot (unlike McpTool/McpResource), so a user's
                // duplicated copy (DuplicateMcpPromptCommand, US5) keeps a stable source to diff
                // against and this row's identity never churns across routine refreshes.
                existingPrompt.RefreshFromSnapshot(snapshot.Id, discoveredPrompt.Description, content);
            }
            else
            {
                var prompt = McpPrompt.CreateFromDiscovery(server.Id, snapshot.Id, discoveredPrompt.Name, discoveredPrompt.Description, content);
                promptRepository.Add(prompt);
            }
        }

        server.RecordCapabilityDiscovery(DateTime.UtcNow);

        var changeSummary = JsonSerializer.Serialize(new { added, changed, removed = removedToolCount });
        auditLogRepository.Add(McpAuditLog.Record(server.Id, userId, McpAuditAction.CapabilityDiscoverySucceeded, null, changeSummary));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new McpCapabilityRefreshResultDto(true, changeSummary, discoveredTools.Count, discoveredResources?.Count ?? 0, discoveredPrompts?.Count ?? 0);
    }

    /// <summary>
    /// Resources and Prompts are optional MCP capabilities — a server that doesn't support one
    /// (or errors listing it) never fails the whole discovery run over an optional capability;
    /// only <see cref="IMcpClient.ListToolsAsync"/> failing fails the run (FR-011's primary
    /// capability). Returns <see langword="null"/> (not an empty list) on failure, so the caller
    /// can distinguish "the server declared zero items" from "this capability wasn't attempted."
    /// </summary>
    private static async Task<IReadOnlyList<T>?> TryListAsync<T>(Func<Task<IReadOnlyList<T>>> list)
    {
        try
        {
            return await list();
        }
        catch
        {
            return null;
        }
    }

    private static async Task<int> MarkRemovedAsync(
        Guid serverId, Guid newSnapshotId, HashSet<string> currentToolNames, IMcpToolRepository toolRepository, CancellationToken cancellationToken)
    {
        var allServerTools = await toolRepository.ListByServerIdAsync(serverId, cancellationToken);
        var removed = 0;
        foreach (var existing in allServerTools)
        {
            if (existing.McpCapabilitySnapshotId != newSnapshotId && existing.IsAvailable && !currentToolNames.Contains(existing.ToolName))
            {
                existing.MarkUnavailable();
                removed++;
            }
        }

        return removed;
    }
}
