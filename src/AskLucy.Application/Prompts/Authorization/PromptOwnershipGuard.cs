using AskLucy.Domain.Prompts;

namespace AskLucy.Application.Prompts.Authorization;

/// <summary>Centralizes the "does this prompt belong to this caller" check (spec.md FR-090), mirrors <c>MemoryOwnershipGuard</c>/<c>ChatOwnershipGuard</c>. Denial looks like not-found — a request naming a prompt the caller doesn't own returns 404, never 403, avoiding existence disclosure.</summary>
public static class PromptOwnershipGuard
{
    public static Prompt EnsureOwnedBy(Prompt? prompt, string userId)
    {
        if (prompt is null || prompt.OwnerId != userId)
        {
            throw new KeyNotFoundException("Prompt not found.");
        }

        return prompt;
    }
}
