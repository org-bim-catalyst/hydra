using AskLucy.Application.Abstractions;
using AskLucy.Application.Mcp.Commands.RefreshMcpCapabilities;
using AskLucy.Application.Mcp.Resilience;
using Microsoft.Extensions.Logging;

namespace AskLucy.Infrastructure.Mcp;

internal static partial class McpCapabilityRefreshJobLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Capability refresh failed for MCP server {McpServerId} — will retry next cycle")]
    public static partial void RefreshFailed(ILogger logger, Guid mcpServerId, Exception exception);
}

/// <summary>
/// Hangfire recurring job (spec.md FR-013, research.md Decision 10) — calls the exact same
/// <see cref="RefreshMcpCapabilitiesCommandHandler"/> the on-demand "Refresh capabilities" admin
/// action uses, for every enabled server whose own <c>CapabilityRefreshIntervalMinutes</c> has
/// elapsed since <c>LastCapabilityDiscoveryAtUtc</c> — a per-server cadence, not a single global
/// interval, since <c>McpServer.Register</c> lets an administrator configure it per server.
/// </summary>
public sealed class McpCapabilityRefreshJob(
    IMcpServerRepository serverRepository,
    IMcpToolRepository toolRepository,
    IMcpResourceRepository resourceRepository,
    IMcpPromptRepository promptRepository,
    IMcpAuditLogRepository auditLogRepository,
    IMcpClientFactory clientFactory,
    McpConnectionResiliencePolicy resiliencePolicy,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<McpCapabilityRefreshJob> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var handler = new RefreshMcpCapabilitiesCommandHandler(
            serverRepository, toolRepository, resourceRepository, promptRepository, auditLogRepository,
            clientFactory, resiliencePolicy, unitOfWork, new SystemCurrentUserAccessor());

        var serverIds = await serverRepository.ListServersDueForCapabilityRefreshAsync(timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        foreach (var serverId in serverIds)
        {
            try
            {
                await handler.Handle(new RefreshMcpCapabilitiesCommand(serverId), cancellationToken);
            }
            catch (Exception ex)
            {
                // One server's refresh failing must not block the rest of the sweep, and — per
                // FR-016 — the handler itself already leaves that server's prior successful
                // snapshot untouched on failure.
                McpCapabilityRefreshJobLog.RefreshFailed(logger, serverId, ex);
            }
        }
    }
}
