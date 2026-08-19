using AskLucy.Application.Abstractions;
using AskLucy.Domain.Mcp;
using MediatR;

namespace AskLucy.Application.Mcp.Commands.EnableMcpServer;

public sealed class EnableMcpServerCommandHandler(
    IMcpServerRepository serverRepository,
    IMcpAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<EnableMcpServerCommand, McpServerDto>
{
    public async Task<McpServerDto> Handle(EnableMcpServerCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var server = await serverRepository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"MCP server {request.Id} was not found.");

        server.Enable(userId);
        auditLogRepository.Add(McpAuditLog.Record(server.Id, userId, McpAuditAction.ServerEnabled, null, "{}"));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return McpServerDto.Create(server);
    }
}
