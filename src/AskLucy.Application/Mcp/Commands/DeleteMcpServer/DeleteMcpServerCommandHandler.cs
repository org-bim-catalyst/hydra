using AskLucy.Application.Abstractions;
using AskLucy.Domain.Mcp;
using MediatR;

namespace AskLucy.Application.Mcp.Commands.DeleteMcpServer;

public sealed class DeleteMcpServerCommandHandler(
    IMcpServerRepository serverRepository,
    IMcpAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<DeleteMcpServerCommand>
{
    public async Task Handle(DeleteMcpServerCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var server = await serverRepository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"MCP server {request.Id} was not found.");

        // Clarification — removal is strictly blocked while any agent still references this
        // server's tools, listing every reference (research.md Decision 15).
        var referencingAgentTools = await serverRepository.ListReferencingAgentToolsAsync(server.Id, cancellationToken);
        if (referencingAgentTools.Count > 0)
        {
            auditLogRepository.Add(McpAuditLog.Record(server.Id, userId, McpAuditAction.ServerRemovalBlocked, null, $"{{\"referenceCount\":{referencingAgentTools.Count}}}"));
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw new McpServerHasReferencesException(referencingAgentTools);
        }

        server.SoftDelete(userId);
        auditLogRepository.Add(McpAuditLog.Record(server.Id, userId, McpAuditAction.ServerRemoved, null, "{}"));

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
