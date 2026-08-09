using AskLucy.Application.Common;
using AskLucy.Application.Projects.Commands.CreateProject;
using AskLucy.Application.Projects.Commands.DeleteProject;
using AskLucy.Application.Projects.Commands.RenameProject;
using AskLucy.Application.Projects.Queries.ListProjects;
using AskLucy.Web.Contracts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>contracts/projects-api.md, spec.md FR-002a, User Story 5. Rate-limited via `memory-endpoints` — Projects is a lightweight grouping construct introduced for Memory scoping, not a separate cost-tiered surface (research.md Decision 17).</summary>
[ApiController]
[Authorize]
[EnableRateLimiting("memory-endpoints")]
[Route("api/v1/projects")]
public sealed class ProjectsController(ISender mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<ProjectListItemDto>>> List(
        [FromQuery] string? cursor = null, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(new ListProjectsQuery(cursor, pageSize), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<ProjectDto>> Create(CreateProjectRequest request, CancellationToken cancellationToken)
    {
        var project = await mediator.Send(new CreateProjectCommand(request.Name), cancellationToken);
        return CreatedAtAction(nameof(List), new { }, project);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Rename(Guid id, RenameProjectRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(new RenameProjectCommand(id, request.Name), cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteProjectCommand(id), cancellationToken);
        return NoContent();
    }
}
