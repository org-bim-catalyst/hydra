using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Mcp.Queries.ListAvailableMcpPrompts;

public sealed class ListAvailableMcpPromptsQueryHandler(IMcpPromptRepository promptRepository)
    : IRequestHandler<ListAvailableMcpPromptsQuery, IReadOnlyList<McpPromptCatalogSummaryDto>>
{
    public async Task<IReadOnlyList<McpPromptCatalogSummaryDto>> Handle(ListAvailableMcpPromptsQuery request, CancellationToken cancellationToken)
    {
        var rows = await promptRepository.ListAvailableAsync(cancellationToken);
        return rows.Select(r => McpPromptCatalogSummaryDto.Create(r.Prompt, r.ServerName)).ToList();
    }
}
