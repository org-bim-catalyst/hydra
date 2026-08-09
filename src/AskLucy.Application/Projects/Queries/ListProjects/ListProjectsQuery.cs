using AskLucy.Application.Common;
using MediatR;

namespace AskLucy.Application.Projects.Queries.ListProjects;

public sealed record ProjectListItemDto(Guid Id, string Name, DateTime CreatedAtUtc);

/// <summary>contracts/projects-api.md — `GET /api/v1/projects` (spec.md FR-002a), newest-first.</summary>
public sealed record ListProjectsQuery(string? Cursor, int PageSize = 50) : IRequest<PagedResult<ProjectListItemDto>>;
