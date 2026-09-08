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

    /// <summary>
    /// specs/045-conversational-agent-runtime research.md D11 — geocodes and scores one place
    /// name that intent has ALREADY been decided for; performs no classification of its own.
    /// <para>
    /// <c>ResolveLocationCapability</c> is the sole caller. The conversational turn's own decide
    /// step now answers "is this a location request" — the question <see cref="ResolveAsync"/>'s
    /// classifier used to answer — so calling <see cref="ResolveAsync"/> from that path would
    /// classify the same message twice. This method is the same geocoding, confidence scoring
    /// and WGS-84 validation <see cref="ResolveAsync"/> uses after its own classifier runs,
    /// reachable directly.
    /// </para>
    /// </summary>
    Task<LocationResolutionOutcome> ResolveQueryAsync(
        Guid userChatId, string query, CancellationToken cancellationToken = default);
}
