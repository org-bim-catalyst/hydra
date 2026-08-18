using AskLucy.Application.Prompts;
using AskLucy.Application.Prompts.Commands.CreateFolder;
using AskLucy.Application.Prompts.Commands.DeleteFolder;
using AskLucy.Application.Prompts.Commands.MoveFolder;
using AskLucy.Application.Prompts.Commands.RenameFolder;
using AskLucy.Application.Prompts.Queries.GetFolderTree;
using AskLucy.Web.Contracts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>Mirrors <c>KnowledgeBasesController</c>'s folder sub-resource shape exactly (research.md Decision 5, contracts/prompts-api.md).</summary>
[ApiController]
[Authorize]
[EnableRateLimiting("prompt-endpoints")]
[Route("api/v1/prompt-folders")]
public sealed class PromptFoldersController(ISender mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PromptFolderDto>>> GetTree(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetFolderTreeQuery(), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<PromptFolderDto>> Create(CreatePromptFolderRequest request, CancellationToken cancellationToken)
    {
        var folder = await mediator.Send(new CreateFolderCommand(request.Name, request.ParentFolderId), cancellationToken);
        return CreatedAtAction(nameof(GetTree), folder);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PromptFolderDto>> Rename(Guid id, RenamePromptFolderRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new RenameFolderCommand(id, request.Name), cancellationToken));

    [HttpPut("{id:guid}/move")]
    public async Task<ActionResult<PromptFolderDto>> Move(Guid id, MovePromptFolderRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new MoveFolderCommand(id, request.NewParentFolderId), cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteFolderCommand(id), cancellationToken);
        return NoContent();
    }
}
