using AskLucy.Domain.Chats;

namespace AskLucy.Application.Chats.Authorization;

/// <summary>
/// Centralizes the "does this chat belong to this caller" check shared by every
/// chat-mutating handler (FR-018, User Story 3), instead of duplicating it per handler.
/// A plain static guard rather than an ASP.NET Core <c>IAuthorizationHandler</c>/
/// <c>IAuthorizationRequirement</c> deliberately — those types live in
/// <c>Microsoft.AspNetCore.Authorization</c>, which Application must not reference
/// (constitution &#167;3, Dependency Rule).
/// </summary>
public static class ChatOwnershipGuard
{
    public static UserChat EnsureOwnedBy(UserChat? chat, string userId)
    {
        if (chat is null || !chat.IsOwnedBy(userId))
        {
            throw new KeyNotFoundException("Chat not found.");
        }

        return chat;
    }
}
