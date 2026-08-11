using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Mcp.Queries.GetMcpServer;

public sealed class GetMcpServerQueryHandler(IMcpServerRepository serverRepository)
    : IRequestHandler<GetMcpServerQuery, McpServerDto>
{
    public async Task<McpServerDto> Handle(GetMcpServerQuery request, CancellationToken cancellationToken)
    {
        var server = await serverRepository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"MCP server {request.Id} was not found.");

        return McpServerDto.Create(server);
    }
}
