using AskLucy.Application.Abstractions;
using MediatR;
using MemoryEntity = AskLucy.Domain.Memory.Memory;

namespace AskLucy.Application.Memory.Queries.ListMemories;

public sealed class ListMemoriesQueryHandler(
    IMemoryRepository memoryRepository, IProjectRepository projectRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<ListMemoriesQuery, MemoryListResult>
{
    public async Task<MemoryListResult> Handle(ListMemoriesQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var afterId = Guid.TryParse(request.Cursor, out var parsed) ? parsed : (Guid?)null;

        var memories = await memoryRepository.SearchAsync(
            userId, request.Category, request.State, request.ProjectId, request.GeneralOnly,
            request.Query, afterId, request.PageSize, cancellationToken);

        var totalCount = await memoryRepository.CountAsync(
            userId, request.Category, request.State, request.ProjectId, request.GeneralOnly, request.Query, cancellationToken);

        var projectNamesById = await ResolveProjectNamesAsync(memories, userId, cancellationToken);

        var items = memories.Select(m => ToDto(m, projectNamesById)).ToList();
        var nextCursor = items.Count == request.PageSize ? items[^1].Id.ToString() : null;

        return new MemoryListResult(items, nextCursor, totalCount);
    }

    private async Task<Dictionary<Guid, string>> ResolveProjectNamesAsync(
        IReadOnlyList<MemoryEntity> memories, string userId, CancellationToken cancellationToken)
    {
        var projectIds = memories.Where(m => m.ProjectId is not null).Select(m => m.ProjectId!.Value).Distinct().ToList();
        var namesById = new Dictionary<Guid, string>();

        foreach (var projectId in projectIds)
        {
            var project = await projectRepository.GetByIdAsync(projectId, cancellationToken);
            if (project is not null && project.IsOwnedBy(userId))
            {
                namesById[projectId] = project.Name;
            }
        }

        return namesById;
    }

    private static MemoryListItemDto ToDto(MemoryEntity memory, IReadOnlyDictionary<Guid, string> projectNamesById) =>
        new(
            memory.Id, memory.Category.ToString(), memory.Content, memory.State.ToString(), memory.IsSensitive,
            memory.ProjectId, memory.ProjectId is { } pid && projectNamesById.TryGetValue(pid, out var name) ? name : null,
            memory.SourceType.ToString(), memory.SourceConversationId, memory.Importance, memory.Confidence,
            memory.LastReinforcedAtUtc, memory.CreatedAtUtc);
}
