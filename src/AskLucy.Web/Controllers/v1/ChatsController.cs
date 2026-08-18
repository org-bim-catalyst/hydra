using System.Text.Json;
using AskLucy.Application.Chats;
using AskLucy.Application.Chats.Commands.ArchiveUserChat;
using AskLucy.Application.Chats.Commands.ClearUserChatMessages;
using AskLucy.Application.Chats.Commands.CreateUserChat;
using AskLucy.Application.Chats.Commands.DeleteUserChat;
using AskLucy.Application.Chats.Commands.DuplicateUserChat;
using AskLucy.Application.Chats.Commands.FavoriteUserChat;
using AskLucy.Application.Chats.Commands.PinUserChat;
using AskLucy.Application.Chats.Commands.PurgeUserChat;
using AskLucy.Application.Chats.Commands.RenameUserChat;
using AskLucy.Application.Chats.Commands.RestoreUserChat;
using AskLucy.Application.Chats.Commands.UnfavoriteUserChat;
using AskLucy.Application.Chats.Commands.UnpinUserChat;
using AskLucy.Application.Chats.Commands.UpdateChatModelSelection;
using AskLucy.Application.Chats.Queries.ExportUserChat;
using AskLucy.Application.Chats.Queries.GetChatMessages;
using AskLucy.Application.Chats.Queries.SearchUserChats;
using AskLucy.Application.Common;
using AskLucy.Application.Memory.Queries.GetMemoryReferences;
using AskLucy.Application.Projects.Commands.AssignConversationToProject;
using AskLucy.Web.Contracts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>FR-008/FR-033 — every operation is implicitly scoped to the caller (FR-018, User Story 3).</summary>
[ApiController]
[Authorize]
[EnableRateLimiting("chat-endpoints")]
[Route("api/v1/chats")]
public sealed class ChatsController(ISender mediator) : ControllerBase
{
    /// <summary>Search/filter/sort/paginate the caller's own conversations (FR-019–FR-024, contracts/chats-api.md).</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<UserChatSummaryDto>>> Search(
        [FromQuery] ConversationView view = ConversationView.Active,
        [FromQuery] bool? pinned = null,
        [FromQuery] bool? favorite = null,
        [FromQuery] string? q = null,
        [FromQuery] ConversationSort sort = ConversationSort.Newest,
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(new SearchUserChatsQuery(view, pinned, favorite, q, sort, cursor, pageSize), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<UserChatDto>> Create(CreateChatRequest request, CancellationToken cancellationToken)
    {
        var chat = await mediator.Send(new CreateUserChatCommand(request.Title, request.SessionId), cancellationToken);
        return CreatedAtAction(nameof(Search), new { }, chat);
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

    /// <summary>specs/005-multi-provider-ai-engine FR-009 — applies to messages sent after this call only; prior messages keep their original attribution (FR-011).</summary>
    [HttpPatch("{id:guid}/model-selection")]
    public async Task<IActionResult> UpdateModelSelection(Guid id, UpdateChatModelSelectionRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(
            new UpdateChatModelSelectionCommand(id, request.ProviderId, request.ModelId, request.GenerationParameters), cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/messages")]
    public async Task<ActionResult<PagedResult<MessageDto>>> GetMessages(
        Guid id, [FromQuery] string? cursor = null, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(new GetChatMessagesQuery(id, cursor, pageSize), cancellationToken));

    /// <summary>specs/018-ai-memory-system, FR-014 — the "why does Lucy know this" trace for one assistant message (contracts/memories-api.md).</summary>
    [HttpGet("{id:guid}/messages/{messageId:guid}/memory-references")]
    public async Task<ActionResult<IReadOnlyList<MemoryReferenceDto>>> GetMemoryReferences(Guid id, Guid messageId, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetMemoryReferencesQuery(id, messageId), cancellationToken));

    /// <summary>specs/018-ai-memory-system, FR-002a — assigns (or, with a null `projectId`, removes) this conversation's Project (contracts/projects-api.md). Mirrors the existing `PUT /api/v1/chats/{id}/knowledge-bases` sub-resource-action shape from specs/016.</summary>
    [HttpPut("{id:guid}/project")]
    public async Task<IActionResult> AssignProject(Guid id, AssignProjectRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(new AssignConversationToProjectCommand(id, request.ProjectId), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/actions/archive")]
    public async Task<ActionResult<UserChatSummaryDto>> Archive(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ArchiveUserChatCommand(id), cancellationToken));

    [HttpPost("{id:guid}/actions/restore")]
    public async Task<ActionResult<UserChatSummaryDto>> Restore(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new RestoreUserChatCommand(id), cancellationToken));

    [HttpPost("{id:guid}/actions/pin")]
    public async Task<ActionResult<UserChatSummaryDto>> Pin(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new PinUserChatCommand(id), cancellationToken));

    [HttpPost("{id:guid}/actions/unpin")]
    public async Task<ActionResult<UserChatSummaryDto>> Unpin(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new UnpinUserChatCommand(id), cancellationToken));

    [HttpPost("{id:guid}/actions/favorite")]
    public async Task<ActionResult<UserChatSummaryDto>> Favorite(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new FavoriteUserChatCommand(id), cancellationToken));

    [HttpPost("{id:guid}/actions/unfavorite")]
    public async Task<ActionResult<UserChatSummaryDto>> Unfavorite(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new UnfavoriteUserChatCommand(id), cancellationToken));

    [HttpPost("{id:guid}/actions/duplicate")]
    public async Task<ActionResult<UserChatSummaryDto>> Duplicate(Guid id, CancellationToken cancellationToken)
    {
        var duplicate = await mediator.Send(new DuplicateUserChatCommand(id), cancellationToken);
        return CreatedAtAction(nameof(Search), new { }, duplicate);
    }

    [HttpPost("{id:guid}/actions/clear")]
    public async Task<IActionResult> Clear(Guid id, ConfirmActionRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(new ClearUserChatMessagesCommand(id, request.Confirm), cancellationToken);
        return NoContent();
    }

    /// <summary>Permanent delete (FR-004/FR-005) — irreversible; requires explicit confirmation (contracts/chats-api.md).</summary>
    [HttpDelete("{id:guid}/actions/purge")]
    public async Task<IActionResult> Purge(Guid id, ConfirmActionRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(new PurgeUserChatCommand(id, request.Confirm), cancellationToken);
        return NoContent();
    }

    /// <summary>Downloads a structured, portable export of the conversation (FR-025).</summary>
    [HttpGet("{id:guid}/export")]
    public async Task<IActionResult> Export(Guid id, CancellationToken cancellationToken)
    {
        var export = await mediator.Send(new ExportUserChatQuery(id), cancellationToken);
        var fileName = $"{SanitizeFileName(export.Title)}.json";
        return File(JsonSerializer.SerializeToUtf8Bytes(export), "application/json", fileName);
    }

    private static string SanitizeFileName(string title)
    {
        var sanitized = string.Join("_", title.Split(Path.GetInvalidFileNameChars()));
        return string.IsNullOrWhiteSpace(sanitized) ? "conversation" : sanitized;
    }
}
