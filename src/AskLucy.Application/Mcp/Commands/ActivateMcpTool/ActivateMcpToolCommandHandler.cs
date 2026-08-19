using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Domain.Mcp;
using MediatR;

namespace AskLucy.Application.Mcp.Commands.ActivateMcpTool;

public sealed class ActivateMcpToolCommandHandler(
    IMcpToolRepository toolRepository,
    IMcpAuditLogRepository auditLogRepository,
    IMcpToolRegistry mcpToolRegistry,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<ActivateMcpToolCommand, McpToolDto>
{
    public async Task<McpToolDto> Handle(ActivateMcpToolCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var tool = await toolRepository.GetByIdAsync(request.ToolId, cancellationToken)
            ?? throw new KeyNotFoundException($"MCP tool {request.ToolId} was not found.");

        if (tool.McpServerId != request.McpServerId)
        {
            // Route nesting mismatch (contracts/mcp-api.md's parent/child path) — treated as
            // "not found" rather than a 400, mirroring KnowledgeBaseFolderGuard.EnsureBelongsTo.
            throw new KeyNotFoundException($"MCP tool {request.ToolId} was not found under server {request.McpServerId}.");
        }

        tool.Activate(userId, request.EffectiveRiskLevelOverride, request.RequiredPermissionsJsonOverride);
        auditLogRepository.Add(McpAuditLog.Record(tool.McpServerId, userId, McpAuditAction.ToolActivated, null, $"{{\"toolName\":\"{tool.ToolName}\"}}"));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // FR-004/SC-008 — the tool becomes immediately usable, not eventually consistent.
        await mcpToolRegistry.InvalidateAsync(cancellationToken);

        return McpToolDto.Create(tool);
    }
}
