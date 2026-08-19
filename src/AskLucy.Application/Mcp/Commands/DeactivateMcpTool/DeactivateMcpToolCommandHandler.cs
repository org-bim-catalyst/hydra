using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Domain.Mcp;
using MediatR;

namespace AskLucy.Application.Mcp.Commands.DeactivateMcpTool;

public sealed class DeactivateMcpToolCommandHandler(
    IMcpToolRepository toolRepository,
    IMcpAuditLogRepository auditLogRepository,
    IMcpToolRegistry mcpToolRegistry,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<DeactivateMcpToolCommand, McpToolDto>
{
    public async Task<McpToolDto> Handle(DeactivateMcpToolCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var tool = await toolRepository.GetByIdAsync(request.ToolId, cancellationToken)
            ?? throw new KeyNotFoundException($"MCP tool {request.ToolId} was not found.");

        if (tool.McpServerId != request.McpServerId)
        {
            throw new KeyNotFoundException($"MCP tool {request.ToolId} was not found under server {request.McpServerId}.");
        }

        tool.Deactivate(userId);
        auditLogRepository.Add(McpAuditLog.Record(tool.McpServerId, userId, McpAuditAction.ToolDeactivated, null, $"{{\"toolName\":\"{tool.ToolName}\"}}"));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // FR-004/SC-008 — the tool disappears from ActiveTools immediately, not on the next refresh.
        await mcpToolRegistry.InvalidateAsync(cancellationToken);

        return McpToolDto.Create(tool);
    }
}
