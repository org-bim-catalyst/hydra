using MediatR;

namespace AskLucy.Application.Projects.Commands.CreateProject;

public sealed record ProjectDto(Guid Id, string Name, DateTime CreatedAtUtc);

/// <summary>contracts/projects-api.md — `POST /api/v1/projects` (spec.md FR-002a).</summary>
public sealed record CreateProjectCommand(string Name) : IRequest<ProjectDto>;
