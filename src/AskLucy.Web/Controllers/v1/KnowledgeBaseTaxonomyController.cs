using AskLucy.Application.KnowledgeBases;
using AskLucy.Application.KnowledgeBases.Commands.CreateCustomCategory;
using AskLucy.Application.KnowledgeBases.Commands.DeleteCategory;
using AskLucy.Application.KnowledgeBases.Queries.ListCategories;
using AskLucy.Application.KnowledgeBases.Queries.ListTags;
using AskLucy.Web.Contracts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>
/// Categories/tags aren't sub-resources of one knowledge base — they're caller-scoped lists
/// referenced *by* many knowledge bases (FR-017–FR-021, FR-038), so this is a sibling of
/// <see cref="KnowledgeBasesController"/> rather than nested under it
/// (contracts/knowledge-base-taxonomy-api.md).
/// </summary>
[ApiController]
[Authorize]
[EnableRateLimiting("knowledge-base-endpoints")]
[Route("api/v1/knowledge-bases")]
public sealed class KnowledgeBaseTaxonomyController(ISender mediator) : ControllerBase
{
    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<KnowledgeBaseCategoryDto>>> ListCategories(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ListCategoriesQuery(), cancellationToken));

    [HttpPost("categories")]
    public async Task<ActionResult<KnowledgeBaseCategoryDto>> CreateCategory(CreateCustomCategoryRequest request, CancellationToken cancellationToken)
    {
        var category = await mediator.Send(new CreateCustomCategoryCommand(request.Name), cancellationToken);
        return CreatedAtAction(nameof(ListCategories), category);
    }

    [HttpDelete("categories/{id:guid}")]
    public async Task<IActionResult> DeleteCategory(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteCategoryCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpGet("tags")]
    public async Task<ActionResult<IReadOnlyList<string>>> ListTags([FromQuery] string? q, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ListTagsQuery(q), cancellationToken));
}
