using System.Collections.Concurrent;

namespace AskLucy.Application.PointsOfInterest;

/// <summary>What one chat currently has drawn, and what a follow-up choice is answering.</summary>
/// <param name="Layers">Active layers keyed by category key; order is insertion order.</param>
/// <param name="Pending">The keep/replace question outstanding for this chat, if any.</param>
public sealed record PoiChatLayerState(
    IReadOnlyList<PoiCategoryDefinition> Layers,
    PendingPoiRequest? Pending);

/// <summary>Which question Lucy is waiting on an answer to.</summary>
public enum PoiPendingQuestion
{
    /// <summary>"Clear the existing markers or keep them alongside?"</summary>
    KeepOrReplace,

    /// <summary>"Which area should I search?" — asked when no area was named.</summary>
    WhichArea,
}

/// <summary>
/// What the user was asked about, held while they answer. Carries no results: the search runs
/// once, after the answer, so asking does not pay for a second Overpass round trip either way.
/// </summary>
public sealed record PendingPoiRequest(
    string CategoryKey,
    string? AreaName,
    PoiPendingQuestion Question,
    DateTime CreatedAtUtc);

/// <summary>
/// Process-wide, per-chat memory of which POI layers are on screen (specs/090 FR-008/FR-009).
///
/// <para>
/// In-memory by design for the PoC: a layer is view state of one open chat, and the viewer that
/// renders it is in the same process's browser session. Persisting it would add a table and a
/// migration for state that is meaningless after a restart — the markers are gone too. The store
/// is the one place that answers "is another category already displayed?", which is what lets Lucy
/// ask before replacing the user's map instead of silently clearing it.
/// </para>
/// </summary>
public sealed class PoiLayerStateStore
{
    private static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<Guid, PoiChatLayerState> _states = new();
    private readonly TimeProvider _timeProvider;
    private readonly PoiCategoryCatalog _catalog;

    public PoiLayerStateStore(TimeProvider timeProvider, PoiCategoryCatalog catalog)
    {
        _timeProvider = timeProvider;
        _catalog = catalog;
    }

    /// <summary>
    /// Reconciles the server's view with what the client reports is actually drawn. The legend is
    /// the user's control surface — removing a category there must stop the next turn from asking
    /// about a layer that is no longer on screen (specs/090 FR-009).
    /// </summary>
    public void Sync(Guid chatId, IReadOnlyList<ActivePoiCategoryInput>? active)
    {
        if (active is null)
        {
            return;
        }

        var layers = active
            .Select(a => _catalog.FindByKey(a.Key))
            .Where(layer => layer is not null)
            .Select(layer => layer!)
            .ToList();

        Set(chatId, layers, GetPending(chatId));
    }

    /// <summary>Active categories for this chat, oldest first.</summary>
    public IReadOnlyList<PoiCategoryDefinition> GetLayers(Guid chatId) =>
        _states.TryGetValue(chatId, out var state) ? state.Layers : [];

    /// <summary>The outstanding keep/replace question, or null when there is none (or it has expired).</summary>
    public PendingPoiRequest? GetPending(Guid chatId)
    {
        if (!_states.TryGetValue(chatId, out var state) || state.Pending is null)
        {
            return null;
        }

        if (_timeProvider.GetUtcNow() - state.Pending.CreatedAtUtc > PendingLifetime)
        {
            Set(chatId, state.Layers, pending: null);
            return null;
        }

        return state.Pending;
    }

    /// <summary>Records the question Lucy is about to ask, so the answer can be acted on without repeating its arguments.</summary>
    public void SetPending(Guid chatId, PendingPoiRequest pending)
    {
        Set(chatId, GetLayers(chatId), pending);
    }

    /// <summary>
    /// Commits a layer to the chat's on-screen set. <paramref name="keepExisting"/> adds it beside
    /// the others; otherwise it replaces them — the two answers Lucy can receive.
    /// </summary>
    public void ApplyLayer(Guid chatId, PoiCategoryDefinition category, bool keepExisting)
    {
        var layers = keepExisting
            ? [.. GetLayers(chatId).Where(l => !string.Equals(l.Key, category.Key, StringComparison.OrdinalIgnoreCase)), category]
            : new List<PoiCategoryDefinition> { category };

        Set(chatId, layers, pending: null);
    }

    /// <summary>Removes one category — the legend's close button calls this indirectly via the client.</summary>
    public void RemoveLayer(Guid chatId, string categoryKey)
    {
        var layers = GetLayers(chatId)
            .Where(l => !string.Equals(l.Key, categoryKey, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Set(chatId, layers, GetPending(chatId));
    }

    /// <summary>Clears every layer, keeping any outstanding question untouched.</summary>
    public void Clear(Guid chatId)
    {
        Set(chatId, [], GetPending(chatId));
    }

    private void Set(Guid chatId, IReadOnlyList<PoiCategoryDefinition> layers, PendingPoiRequest? pending)
    {
        if (layers.Count == 0 && pending is null)
        {
            _states.TryRemove(chatId, out _);
            return;
        }

        _states[chatId] = new PoiChatLayerState(layers, pending);
    }
}
