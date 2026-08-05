using AskLucy.Application.Retrieval.Commands.UpdateConversationKnowledgeBases;
using AskLucy.Application.Retrieval.Queries.GetConversationKnowledgeBases;
using AskLucy.Web.Contracts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>
/// contracts/conversation-retrieval-api.md — attach/detach a conversation's knowledge bases
/// (specs/016-rag-semantic-search US1 T054). Nested under the same route base as
/// <see cref="ChatsController"/> and sharing its rate-limit policy, since this is conceptually
/// an extension of a conversation's settings, not a standalone resource.
/// </summary>
[ApiController]
[Authorize]
[EnableRateLimiting("chat-endpoints")]
[Route("api/v1/chats")]
public sealed class ConversationKnowledgeBasesController(ISender mediator) : ControllerBase
{
    [HttpPut("{id:guid}/knowledge-bases")]
    public async Task<IActionResult> UpdateKnowledgeBases(Guid id, UpdateConversationKnowledgeBasesRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(new UpdateConversationKnowledgeBasesCommand(id, request.KnowledgeBaseIds), cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/knowledge-bases")]
    public async Task<ActionResult<ConversationKnowledgeBasesResponse>> GetKnowledgeBases(Guid id, CancellationToken cancellationToken)
    {
        var knowledgeBaseIds = await mediator.Send(new GetConversationKnowledgeBasesQuery(id), cancellationToken);
        return Ok(new ConversationKnowledgeBasesResponse(knowledgeBaseIds));
    }
}
