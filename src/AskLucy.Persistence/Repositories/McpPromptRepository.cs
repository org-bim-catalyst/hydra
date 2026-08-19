using AskLucy.Application.Abstractions;
using AskLucy.Domain.Mcp;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class McpPromptRepository(AskLucyDbContext dbContext) : IMcpPromptRepository
{
    public Task<McpPrompt?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.McpPrompts.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<McpPrompt?> GetByNamespacedNameAsync(string namespacedName, CancellationToken cancellationToken = default) =>
        dbContext.McpPrompts.FirstOrDefaultAsync(p => p.NamespacedName == namespacedName, cancellationToken);

    public void Add(McpPrompt prompt) => dbContext.McpPrompts.Add(prompt);

    public async Task<IReadOnlyList<McpPrompt>> ListByServerIdAsync(Guid serverId, CancellationToken cancellationToken = default) =>
        await dbContext.McpPrompts
            .Where(p => p.McpServerId == serverId)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<(McpPrompt Prompt, string ServerName)>> ListAvailableAsync(CancellationToken cancellationToken = default)
    {
        var rows = await (from prompt in dbContext.McpPrompts
                          join server in dbContext.McpServers on prompt.McpServerId equals server.Id
                          where prompt.IsAvailable && server.IsEnabled
                          select new { prompt, server.Name })
            .ToListAsync(cancellationToken);

        return rows.Select(r => (r.prompt, r.Name)).ToList();
    }
}
