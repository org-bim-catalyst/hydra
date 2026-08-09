using MediatR;

namespace AskLucy.Application.Projects.Commands.DeleteProject;

/// <summary>contracts/projects-api.md — `DELETE /api/v1/projects/{id}` (spec.md FR-002a, User Story 5 AC3).</summary>
public sealed record DeleteProjectCommand(Guid ProjectId) : IRequest;
