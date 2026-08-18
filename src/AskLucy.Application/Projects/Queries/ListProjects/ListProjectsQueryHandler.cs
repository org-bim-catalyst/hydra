using AskLucy.Application.Abstractions;
using AskLucy.Application.Common;
using MediatR;

namespace AskLucy.Application.Projects.Queries.ListProjects;

public sealed class ListProjectsQueryHandler(IProjectRepository projectRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<ListProjectsQuery, PagedResult<ProjectListItemDto>>
{
    public async Task<PagedResult<ProjectListItemDto>> Handle(ListProjectsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var afterId = Guid.TryParse(request.Cursor, out var parsed) ? parsed : (Guid?)null;

        var projects = await projectRepository.ListByUserAsync(userId, afterId, request.PageSize, cancellationToken);

        var items = projects.Select(p => new ProjectListItemDto(p.Id, p.Name, p.CreatedAtUtc)).ToList();
        var nextCursor = items.Count == request.PageSize ? items[^1].Id.ToString() : null;

        return new PagedResult<ProjectListItemDto>(items, nextCursor);
    }
}
