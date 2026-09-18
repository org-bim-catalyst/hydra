using System.Security.Cryptography;
using System.Text;
using AskLucy.Application.Abstractions;
using AskLucy.Application.SiteAnalysis;
using AskLucy.Domain.Workflows;
using Microsoft.Extensions.Logging;

namespace AskLucy.Infrastructure.SiteAnalysis;

internal static partial class SystemWorkflowProvisionerLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "System workflow provisioning deferred: the database has pending migrations")]
    public static partial void DeferredPendingMigrations(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "System workflow provisioning deferred: the database was unreachable")]
    public static partial void DeferredUnreachable(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "System workflow '{SystemKey}' created")]
    public static partial void Created(ILogger logger, string systemKey);

    [LoggerMessage(Level = LogLevel.Information, Message = "System workflow '{SystemKey}' upgraded to version {VersionNumber}")]
    public static partial void Upgraded(ILogger logger, string systemKey, int versionNumber);

    [LoggerMessage(Level = LogLevel.Warning, Message = "System workflow '{SystemKey}' lost a concurrent creation race; re-read the winning row")]
    public static partial void ConcurrentCreationLost(ILogger logger, string systemKey);

    [LoggerMessage(Level = LogLevel.Error, Message = "Provisioning system workflow '{SystemKey}' failed")]
    public static partial void ProvisioningFailed(ILogger logger, string systemKey, Exception exception);
}

/// <summary>
/// Upserts the "site-analysis" fan-out workflow: <c>Start → Parallel → [one NativeTool node per
/// specialist] → Merge → End</c> (research.md D1/D12). Mirrors <c>SystemAgentProvisioner</c>
/// exactly, including its defer-on-pending-migrations and concurrent-creation-race handling
/// (tasks.md rule 7) — copied deliberately rather than shared, since the two provision different
/// aggregate types with different publish semantics.
/// </summary>
public sealed class SystemWorkflowProvisioner(
    IWorkflowRepository workflowRepository,
    IUnitOfWork unitOfWork,
    IDatabaseMigrationStatus migrationStatus,
    ISystemAccountProvisioner systemAccountProvisioner,
    ILogger<SystemWorkflowProvisioner> logger)
{
    public const string SystemKey = SiteAnalysisDispatcher.WorkflowSystemKey;

    private const string Actor = "system:workflow-provisioner";
    private const string SystemKeyUniqueIndexName = "IX_Workflows_SystemKey";
    private const string ChangeDescriptionHashPrefix = "definition-hash:";

    /// <summary>
    /// specs/057-site-analysis-agent FR-031 — the extension point. Adding a further specialist is
    /// one entry here (a tool name + a stable node key) and one provisioner branch entry; no other
    /// change to coordination, validation, delivery, persistence, or rehydration.
    /// </summary>
    private static readonly IReadOnlyList<(string ToolName, string NodeKey)> SpecialistBranches =
    [
        ("site_analysis_schematic_image", "schematic_image"),
    ];

    public async Task ProvisionAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (await migrationStatus.HasPendingMigrationsAsync(cancellationToken))
            {
                SystemWorkflowProvisionerLog.DeferredPendingMigrations(logger);
                return;
            }

            // The Workflows.OwnerId foreign key requires this account row to exist before the
            // first provisioning attempt, not be discovered missing by it — same reasoning as
            // SystemAgentProvisioner.
            await systemAccountProvisioner.EnsureSystemAccountExistsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            SystemWorkflowProvisionerLog.DeferredUnreachable(logger, ex);
            return;
        }

        try
        {
            await ProvisionOneAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Logged, never left to crash the host (constitution §2.VIII) — the next startup retries.
            SystemWorkflowProvisionerLog.ProvisioningFailed(logger, SystemKey, ex);
        }
    }

    private async Task ProvisionOneAsync(CancellationToken cancellationToken)
    {
        var hash = ComputeDefinitionHash();
        var workflow = await workflowRepository.GetBySystemKeyAsync(SystemKey, cancellationToken);

        if (workflow is null)
        {
            var candidate = Workflow.CreateSystemProvisioned(SystemKey, "Site Analysis", "Fans out a resolved site to specialist analyses.", WorkflowType.EventDriven, Actor);
            PublishDefinition(candidate, hash);
            workflowRepository.Add(candidate);

            if (await unitOfWork.TrySaveChangesAsync(SystemKeyUniqueIndexName, cancellationToken))
            {
                SystemWorkflowProvisionerLog.Created(logger, SystemKey);
                return;
            }

            // Another instance won the race between our read and our write.
            SystemWorkflowProvisionerLog.ConcurrentCreationLost(logger, SystemKey);
            workflow = await workflowRepository.GetBySystemKeyAsync(SystemKey, cancellationToken)
                ?? throw new InvalidOperationException($"System workflow '{SystemKey}' vanished after losing a creation race.");
        }

        var newestVersion = workflow.Versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
        var newestHash = newestVersion?.ChangeDescription is { } description && description.StartsWith(ChangeDescriptionHashPrefix, StringComparison.Ordinal)
            ? description[ChangeDescriptionHashPrefix.Length..]
            : null;

        if (string.Equals(newestHash, hash, StringComparison.Ordinal))
        {
            return;
        }

        var version = PublishDefinition(workflow, hash);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        SystemWorkflowProvisionerLog.Upgraded(logger, SystemKey, version.VersionNumber);
    }

    /// <summary>Builds and publishes the fan-out graph — Start → Parallel → one NativeTool node per <see cref="SpecialistBranches"/> entry → Merge (tasks.md rule 5: <c>AnyCompleted</c>) → End (research.md D1: every branch is exactly one node, all converging on the same Merge).</summary>
    private static WorkflowVersion PublishDefinition(Workflow workflow, string hash)
    {
        WorkflowNodeSpec Node(string key, WorkflowNodeType type, string config = "{}") =>
            new(key, type, key, null, "{}", "{}", config, "[]", null, null, WorkflowNodeApprovalPolicy.NeverRequire, null, null, 0, 0);

        var nodes = new List<WorkflowNodeSpec>
        {
            Node("start", WorkflowNodeType.Start),
            Node("parallel", WorkflowNodeType.Parallel),
        };
        var connections = new List<WorkflowConnectionSpec> { new("start", "parallel", null, null) };

        foreach (var (toolName, nodeKey) in SpecialistBranches)
        {
            nodes.Add(Node(nodeKey, WorkflowNodeType.NativeTool, BuildNativeToolNodeConfig(toolName)));
            connections.Add(new WorkflowConnectionSpec("parallel", nodeKey, null, null));
            connections.Add(new WorkflowConnectionSpec(nodeKey, "merge", null, null));
        }

        nodes.Add(Node("merge", WorkflowNodeType.Merge, """{"strategy":"AnyCompleted"}"""));
        nodes.Add(Node("end", WorkflowNodeType.End));
        connections.Add(new WorkflowConnectionSpec("merge", "end", null, null));

        return workflow.Publish(nodes, connections, [], "{}", "{}", "{}", "{}", "{}", $"{ChangeDescriptionHashPrefix}{hash}", Actor);
    }

    /// <summary>
    /// Not an interpolated string — <c>{{workflow.siteAnalysisId}}</c> etc. are literal
    /// <c>{{...}}</c> workflow-expression tokens (<c>NativeToolNodeExecutor</c>/
    /// <c>WorkflowCapabilityToolInvoker.TryResolveConfigObject</c>), resolved against
    /// <c>WorkflowResolvedValues.AddFlattened(resolvedValues, "workflow", execution.InputsJson)</c>
    /// at run time — not something C# string interpolation should ever touch.
    /// </summary>
    private static string BuildNativeToolNodeConfig(string toolName)
    {
        const string toolInputTemplate =
            """{"siteAnalysisId":"{{workflow.siteAnalysisId}}","siteName":"{{workflow.siteName}}","siteLocation":"{{workflow.siteLocation}}"}""";

        return "{\"toolName\":\"" + toolName + "\",\"input\":" + toolInputTemplate + "}";
    }

    private static string ComputeDefinitionHash()
    {
        const char separator = '';
        var payload = string.Join(separator, SpecialistBranches.Select(b => $"{b.ToolName}:{b.NodeKey}"));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }
}
