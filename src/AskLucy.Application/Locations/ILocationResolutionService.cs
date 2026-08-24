using AskLucy.Domain.Chats;

namespace AskLucy.Application.Locations;

/// <summary>
/// specs/037-location-query-resolution — classifies location intent in a user message,
/// geocodes when necessary, and returns a <see cref="LocationResolutionOutcome"/> that
/// <c>SendChatMessageCommandHandler</c> appends to the response stream (FR-008: runs
/// concurrently with <c>IAIProvider.StreamChatAsync</c>, never blocking first byte).
/// </summary>
public interface ILocationResolutionService
{
    Task<LocationResolutionOutcome> ResolveAsync(
        string? userId,
        Guid userChatId,
        string latestUserMessage,
        ActiveSiteLocation? activeLocation,
        CancellationToken cancellationToken = default);
}
