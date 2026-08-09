using AskLucy.Application.Abstractions;
using AskLucy.Domain.Projects;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class ProjectRepository(AskLucyDbContext dbContext) : IProjectRepository
{
    public Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Projects.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Project>> ListByUserAsync(string userId, Guid? afterId, int pageSize, CancellationToken cancellationToken = default)
    {
        var items = await dbContext.Projects
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        if (afterId is not null)
        {
            var afterIndex = items.FindIndex(p => p.Id == afterId);
            items = afterIndex >= 0 ? items.Skip(afterIndex + 1).ToList() : items;
        }

        return items.Take(pageSize).ToList();
    }

    public void Add(Project project) => dbContext.Projects.Add(project);
}
