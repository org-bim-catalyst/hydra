using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Mcp.Commands.TestMcpServerConnection;
using AskLucy.Application.Mcp.Resilience;
using Microsoft.Extensions.Logging;

namespace AskLucy.Infrastructure.Mcp;

internal static partial class McpServerHealthCheckJobLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Health check failed for MCP server {McpServerId} — will retry next cycle")]
    public static partial void CheckFailed(ILogger logger, Guid mcpServerId, Exception exception);
}

/// <summary>
/// Hangfire recurring job (spec.md User Story 6, research.md Decision 10) — calls the exact same
/// <see cref="TestMcpServerConnectionCommandHandler"/> the on-demand "Test connection" admin action
/// uses, for every currently-enabled server, no duplicate connect-and-record-health logic.
/// Invalidates <see cref="IMcpToolRegistry"/> once after the sweep so <c>ActiveTools</c> excludes/
/// re-includes tools the moment a server's health leaves/returns to <c>Healthy</c> (FR-056),
/// rather than waiting for an unrelated activation/deactivation to trigger the next rebuild.
/// </summary>
public sealed class McpServerHealthCheckJob(
    IMcpServerRepository serverRepository,
    IMcpAuditLogRepository auditLogRepository,
    IMcpClientFactory clientFactory,
    McpConnectionResiliencePolicy resiliencePolicy,
    IUnitOfWork unitOfWork,
    IMcpToolRegistry mcpToolRegistry,
    ILogger<McpServerHealthCheckJob> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var handler = new TestMcpServerConnectionCommandHandler(
            serverRepository, auditLogRepository, clientFactory, resiliencePolicy, unitOfWork, new SystemCurrentUserAccessor());

        var serverIds = await serverRepository.ListEnabledServerIdsAsync(cancellationToken);
        foreach (var serverId in serverIds)
        {
            try
            {
                await handler.Handle(new TestMcpServerConnectionCommand(serverId), cancellationToken);
            }
            catch (Exception ex)
            {
                // One server's check failing must not block the rest of the sweep.
                McpServerHealthCheckJobLog.CheckFailed(logger, serverId, ex);
            }
        }

        await mcpToolRegistry.InvalidateAsync(cancellationToken);
    }
}
