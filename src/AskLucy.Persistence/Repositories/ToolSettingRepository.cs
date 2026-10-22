using AskLucy.Application.Abstractions;
using AskLucy.Domain.Agents;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class ToolSettingRepository(AskLucyDbContext dbContext) : IToolSettingRepository
{
    public async Task<IReadOnlyList<ToolSetting>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await dbContext.ToolSettings.OrderBy(s => s.ToolName).ThenBy(s => s.Key).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ToolSetting>> ListByToolAsync(string toolName, CancellationToken cancellationToken = default) =>
        await dbContext.ToolSettings.Where(s => s.ToolName == toolName).ToListAsync(cancellationToken);

    public async Task<ToolSetting?> GetAsync(string toolName, string key, CancellationToken cancellationToken = default) =>
        await dbContext.ToolSettings.FirstOrDefaultAsync(s => s.ToolName == toolName && s.Key == key, cancellationToken);

    public void Add(ToolSetting setting) => dbContext.ToolSettings.Add(setting);
}
