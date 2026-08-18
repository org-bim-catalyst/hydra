using AskLucy.Domain.Projects;

namespace AskLucy.Application.Abstractions;

public interface IProjectRepository
{
    Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Project>> ListByUserAsync(string userId, Guid? afterId, int pageSize, CancellationToken cancellationToken = default);

    void Add(Project project);
}
