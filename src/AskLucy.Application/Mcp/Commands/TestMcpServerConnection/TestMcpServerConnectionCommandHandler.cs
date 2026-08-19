using AskLucy.Application.Abstractions;
using AskLucy.Application.Mcp.Resilience;
using AskLucy.Domain.Mcp;
using MediatR;

namespace AskLucy.Application.Mcp.Commands.TestMcpServerConnection;

public sealed class TestMcpServerConnectionCommandHandler(
    IMcpServerRepository serverRepository,
    IMcpAuditLogRepository auditLogRepository,
    IMcpClientFactory clientFactory,
    McpConnectionResiliencePolicy resiliencePolicy,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<TestMcpServerConnectionCommand, McpServerHealthDto>
{
    public async Task<McpServerHealthDto> Handle(TestMcpServerConnectionCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var server = await serverRepository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"MCP server {request.Id} was not found.");

        var health = await serverRepository.GetHealthAsync(server.Id, cancellationToken);
        if (health is null)
        {
            health = McpServerHealth.CreateUnknown(server.Id);
            serverRepository.AddHealth(health);
        }

        McpServerHealthStatus status;
        McpFailureCategory? failureCategory = null;
        string? detail = null;

        try
        {
            await resiliencePolicy.ExecuteAsync(server.Id, isIdempotent: true, async ct =>
            {
                var client = await clientFactory.GetOrCreateAsync(server.Id, ct);
                await client.PingAsync(ct);
                return true;
            }, cancellationToken);

            status = McpServerHealthStatus.Healthy;
            server.RecordHealthCheck(DateTime.UtcNow);
        }
        catch (McpCircuitOpenException)
        {
            status = McpServerHealthStatus.Unavailable;
            failureCategory = McpFailureCategory.ServerUnavailable;
            detail = "The circuit is open after repeated consecutive failures.";
        }
        catch (UnauthorizedAccessException)
        {
            status = McpServerHealthStatus.AuthenticationFailed;
            failureCategory = McpFailureCategory.AuthenticationFailure;
            detail = "Authentication with the MCP server failed.";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            status = McpServerHealthStatus.Unavailable;
            failureCategory = McpFailureCategory.Timeout;
            detail = "The connection attempt timed out.";
        }
        catch (Exception ex)
        {
            status = McpServerHealthStatus.Unavailable;
            failureCategory = McpFailureCategory.ConnectionFailure;
            // FR-046/FR-059 — never a raw exception message that could contain credential
            // material or an internal stack detail; a short, safe, actionable summary only.
            detail = "The server could not be reached.";
            _ = ex;
        }

        health.RecordCheck(status, failureCategory, detail);
        auditLogRepository.Add(McpAuditLog.Record(server.Id, userId, McpAuditAction.HealthStateChanged, failureCategory, $"{{\"status\":\"{status}\"}}"));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return McpServerHealthDto.Create(health);
    }
}
