using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Mcp.Queries.GetMcpTool;

public sealed class GetMcpToolQueryHandler(IMcpToolRepository toolRepository)
    : IRequestHandler<GetMcpToolQuery, McpToolDetailDto>
{
    public async Task<McpToolDetailDto> Handle(GetMcpToolQuery request, CancellationToken cancellationToken)
    {
        var row = await toolRepository.GetActiveAvailableByNamespacedNameAsync(request.NamespacedName, cancellationToken)
            ?? throw new KeyNotFoundException($"MCP tool '{request.NamespacedName}' was not found.");

        return McpToolDetailDto.Create(row.Tool, row.ServerName);
    }
}
