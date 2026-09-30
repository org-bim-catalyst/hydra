using AskLucy.Domain.Chats;

namespace AskLucy.Application.SiteBoundaries;

/// <summary>
/// specs/079 (research D5) — the outline in force for a chat. A chat keeps the outline Lucy found
/// and may link to the user's hand-edited <c>SiteBoundaryCorrection</c>; every reader that needs
/// the site's shape asks here instead of reading <c>UserChat.ActiveBoundary</c> directly, so a
/// hand edit applies everywhere (FR-025). A link to a correction that no longer exists (a reset)
/// or is not the chat owner's is dead and ignored, so the chat shows its outline as found.
/// </summary>
public sealed class EffectiveSiteBoundary(ISiteBoundaryCorrectionRepository corrections)
{
    public async Task<ActiveSiteBoundary?> ResolveAsync(UserChat? chat, CancellationToken cancellationToken = default)
    {
        var found = chat?.ActiveBoundary;
        if (chat is null || found?.CorrectionId is not { } correctionId)
        {
            return found;
        }

        var correction = await corrections.GetByIdAsync(correctionId, chat.UserId, cancellationToken);
        return correction is null ? found : found.WithCorrection(correction);
    }
}
