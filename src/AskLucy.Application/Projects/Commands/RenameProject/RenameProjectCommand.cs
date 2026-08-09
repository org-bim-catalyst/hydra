using MediatR;

namespace AskLucy.Application.Projects.Commands.RenameProject;

/// <summary>contracts/projects-api.md — `PUT /api/v1/projects/{id}` (spec.md FR-002a).</summary>
public sealed record RenameProjectCommand(Guid ProjectId, string Name) : IRequest;
