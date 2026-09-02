using AskLucy.Domain.Chats;
using Microsoft.Extensions.DependencyInjection;

namespace AskLucy.Application.Locations;

/// <summary>
/// Runs <see cref="LocationResolutionService"/> inside its own DI scope, so the location work
/// that <see cref="ILocationResolutionService"/> documents as running "concurrently with
/// IAIProvider.StreamChatAsync, never blocking first byte" (specs/037 FR-008) does not share the
/// request's scoped <c>DbContext</c> with the thread that is streaming the model's reply.
/// <para>
/// Without this, the two race. Observed in production 2026-08-30:
/// <c>SendChatMessageCommandHandler</c> starts the location task, then immediately calls
/// <c>StreamChatAsync</c>; both paths reach <c>AnthropicProvider.CreateClientAsync</c>, which
/// reads the provider row to decrypt its credential, and EF Core threw
/// "A second operation was started on this context instance before a previous operation
/// completed" — an unclassified exception, so the whole turn came back as a bare HTTP 500
/// rather than any of the actionable statuses <c>AiProviderResponseClassifier</c> produces.
/// </para>
/// <para>
/// It stayed hidden for as long as the platform default was OpenAI:
/// <c>OpenAiProvider.CreateClientAsync</c> reads its key from configuration and touches no
/// DbContext at all, so the concurrent task never issued a query for the stream to collide
/// with. Anthropic and Google Gemini both read the encrypted credential from the database, so
/// selecting either one made the race reachable on the very first message.
/// </para>
/// <para>
/// Kept as a decorator rather than an <c>IServiceScopeFactory</c> in the handler so the
/// isolation travels with the interface — any future caller of this concurrency-by-contract
/// service gets it too, and the handler and its tests stay unaware of scoping entirely.
/// </para>
/// </summary>
public sealed class ScopeIsolatedLocationResolutionService(IServiceScopeFactory scopeFactory) : ILocationResolutionService
{
    public async Task<LocationResolutionOutcome> ResolveAsync(
        string? userId,
        Guid userChatId,
        string latestUserMessage,
        ActiveSiteLocation? activeLocation,
        CancellationToken cancellationToken = default)
    {
        // Disposed once the inner call completes. Safe because LocationResolutionOutcome and
        // everything it carries are plain values — nothing here is a tracked entity that would
        // outlive the scope that loaded it. activeLocation comes from the caller's context and
        // is only read.
        using var scope = scopeFactory.CreateScope();
        var inner = scope.ServiceProvider.GetRequiredService<LocationResolutionService>();
        return await inner.ResolveAsync(userId, userChatId, latestUserMessage, activeLocation, cancellationToken);
    }
}
