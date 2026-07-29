using AskLucy.Application.Chats;
using AskLucy.Application.Chats.Commands.CreateUserChat;
using AskLucy.Application.Chats.Commands.DeleteUserChat;
using AskLucy.Application.Chats.Commands.RenameUserChat;
using AskLucy.Application.Chats.Queries.GetChatMessages;
using AskLucy.Application.Chats.Queries.GetMyUserChats;
using AskLucy.Web.Contracts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AskLucy.Web.Controllers.v1;

/// <summary>FR-008/FR-033 — every operation is implicitly scoped to the caller (FR-018, User Story 3).</summary>
[ApiController]
[Authorize]
[Route("api/v1/chats")]
public sealed class ChatsController(ISender mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserChatDto>>> GetMine(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetMyUserChatsQuery(), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<UserChatDto>> Create(CreateChatRequest request, CancellationToken cancellationToken)
    {
        var chat = await mediator.Send(new CreateUserChatCommand(request.Title, request.SessionId), cancellationToken);
        return CreatedAtAction(nameof(GetMine), new { }, chat);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<UserChatDto>> Rename(Guid id, RenameChatRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new RenameUserChatCommand(id, request.Title), cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteUserChatCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/messages")]
    public async Task<ActionResult<IReadOnlyList<MessageDto>>> GetMessages(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetChatMessagesQuery(id), cancellationToken));
}
