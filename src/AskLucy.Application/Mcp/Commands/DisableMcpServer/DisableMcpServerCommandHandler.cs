using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Domain.Mcp;
using MediatR;

namespace AskLucy.Application.Mcp.Commands.DisableMcpServer;

public sealed class DisableMcpServerCommandHandler(
    IMcpServerRepository serverRepository,
    IMcpAuditLogRepository auditLogRepository,
    IMcpToolRegistry mcpToolRegistry,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<DisableMcpServerCommand, McpServerDto>
{
    public async Task<McpServerDto> Handle(DisableMcpServerCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var server = await serverRepository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"MCP server {request.Id} was not found.");

        server.Disable(userId);
        auditLogRepository.Add(McpAuditLog.Record(server.Id, userId, McpAuditAction.ServerDisabled, null, "{}"));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // FR-004/SC-008 — every tool from this server is immediately absent from ActiveTools,
        // not just eventually consistent on the next scheduled refresh.
        await mcpToolRegistry.InvalidateAsync(cancellationToken);

        return McpServerDto.Create(server);
    }
}
