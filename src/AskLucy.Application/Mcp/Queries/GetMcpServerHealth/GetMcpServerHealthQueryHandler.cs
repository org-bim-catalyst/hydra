using AskLucy.Application.Abstractions;
using AskLucy.Domain.Mcp;
using MediatR;

namespace AskLucy.Application.Mcp.Queries.GetMcpServerHealth;

public sealed class GetMcpServerHealthQueryHandler(IMcpServerRepository serverRepository)
    : IRequestHandler<GetMcpServerHealthQuery, McpServerHealthDto>
{
    public async Task<McpServerHealthDto> Handle(GetMcpServerHealthQuery request, CancellationToken cancellationToken)
    {
        _ = await serverRepository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"MCP server {request.Id} was not found.");

        var health = await serverRepository.GetHealthAsync(request.Id, cancellationToken);

        // A registered server that has never been checked yet still has a valid health state —
        // Unknown, not a 404 — until the first TestMcpServerConnection/scheduled check runs.
        return McpServerHealthDto.Create(health ?? McpServerHealth.CreateUnknown(request.Id));
    }
}
