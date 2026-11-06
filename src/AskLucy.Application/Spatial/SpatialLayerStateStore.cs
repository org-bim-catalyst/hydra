using System.Collections.Concurrent;

namespace AskLucy.Application.Spatial;

/// <summary>One layer currently known to be drawn for a chat, in insertion order.</summary>
public sealed record SpatialLayerStateEntry(
    string AnalysisId,
    string LayerId,
    string Name,
    string Kind,
    string LegendLabel);

/// <summary>Which question Lucy is waiting on an answer to.</summary>
public enum SpatialPendingQuestion
{
    /// <summary>"Clear the existing layers or keep them alongside?"</summary>
    KeepOrReplace,

    /// <summary>"Which area should I search?" — asked when no area could be resolved.</summary>
    WhichArea,
}

/// <summary>
/// What the user was asked about, held while they answer. Carries no results: the analysis runs
/// once, after the answer, so asking does not pay for a second provider round trip either way.
/// </summary>
public sealed record PendingSpatialRequest(
    string OperationKind,
    string? CategoryKey,
    string? AreaName,
    SpatialPendingQuestion Question,
    DateTime CreatedAtUtc);

/// <summary>What one chat currently has drawn, and what a follow-up choice is answering.</summary>
public sealed record SpatialChatLayerState(
    IReadOnlyList<SpatialLayerStateEntry> Layers,
    PendingSpatialRequest? Pending);

/// <summary>
/// Process-wide, per-chat memory of which spatial layers are on screen (FR-020/FR-021). The
/// generalization of spec 090's <c>PoiLayerStateStore</c>: layers are keyed by analysis + layer,
/// so one analysis can contribute several layers and several analyses can coexist.
/// <para>
/// In-memory by design: a layer is view state of one open chat, and the viewer that renders it is
/// in the same process's browser session. Persisting it would add a table and a migration for
/// state that is meaningless after a restart — the markers are gone too.
/// </para>
/// </summary>
public sealed class SpatialLayerStateStore
{
    private static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<Guid, SpatialChatLayerState> _states = new();
    private readonly TimeProvider _timeProvider;

    public SpatialLayerStateStore(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Reconciles the server's view with what the client reports is actually drawn. The legend is
    /// the user's control surface — removing a layer there must stop the next turn from asking
    /// about a layer that is no longer on screen.
    /// </summary>
    public void Sync(Guid chatId, IReadOnlyList<ActiveSpatialLayer>? active)
    {
        if (active is null)
        {
            return;
        }

        var layers = active
            .Where(layer => !string.IsNullOrWhiteSpace(layer.LayerId))
            .Select(layer => new SpatialLayerStateEntry(
                layer.AnalysisId,
                layer.LayerId,
                layer.Name,
                layer.Kind,
                layer.LegendLabel))
            .ToList();

        Set(chatId, layers, GetPending(chatId));
    }

    public IReadOnlyList<SpatialLayerStateEntry> GetLayers(Guid chatId) =>
        _states.TryGetValue(chatId, out var state) ? state.Layers : [];

    /// <summary>The outstanding question, or null when there is none (or it has expired).</summary>
    public PendingSpatialRequest? GetPending(Guid chatId)
    {
        if (!_states.TryGetValue(chatId, out var state) || state.Pending is null)
        {
            return null;
        }

        if (_timeProvider.GetUtcNow().UtcDateTime - state.Pending.CreatedAtUtc > PendingLifetime)
        {
            Set(chatId, state.Layers, pending: null);
            return null;
        }

        return state.Pending;
    }

    public void SetPending(Guid chatId, PendingSpatialRequest pending) =>
        Set(chatId, GetLayers(chatId), pending);

    /// <summary>
    /// Commits an analysis's layers to the chat's on-screen set. <paramref name="keepExisting"/>
    /// draws them beside other analyses; otherwise it replaces everything. Layers belonging to the
    /// same analysis are always replaced, so a repeated search never stacks duplicates.
    /// </summary>
    public void ApplyLayers(Guid chatId, string analysisId, IReadOnlyList<SpatialLayer> layers, bool keepExisting)
    {
        var baseLayers = keepExisting
            ? GetLayers(chatId).Where(layer => !string.Equals(layer.AnalysisId, analysisId, StringComparison.Ordinal)).ToList()
            : [];

        baseLayers.AddRange(layers.Select(layer => new SpatialLayerStateEntry(
            analysisId,
            layer.LayerId,
            layer.Name,
            KindText(layer.Kind),
            layer.LegendLabel)));

        Set(chatId, baseLayers, pending: null);
    }

    /// <summary>Removes one layer — the legend's close button.</summary>
    public void RemoveLayer(Guid chatId, string layerId)
    {
        var layers = GetLayers(chatId)
            .Where(layer => !string.Equals(layer.LayerId, layerId, StringComparison.Ordinal))
            .ToList();

        Set(chatId, layers, GetPending(chatId));
    }

    /// <summary>Removes every layer belonging to one analysis — "clear this analysis".</summary>
    public void ClearAnalysis(Guid chatId, string analysisId)
    {
        var layers = GetLayers(chatId)
            .Where(layer => !string.Equals(layer.AnalysisId, analysisId, StringComparison.Ordinal))
            .ToList();

        Set(chatId, layers, GetPending(chatId));
    }

    /// <summary>Clears every layer, keeping any outstanding question untouched.</summary>
    public void Clear(Guid chatId) => Set(chatId, [], GetPending(chatId));

    private static string KindText(SpatialLayerKind kind) => kind switch
    {
        SpatialLayerKind.Markers => "markers",
        SpatialLayerKind.Buffer => "buffer",
        SpatialLayerKind.Area => "area",
        _ => "band",
    };

    private void Set(Guid chatId, IReadOnlyList<SpatialLayerStateEntry> layers, PendingSpatialRequest? pending)
    {
        if (layers.Count == 0 && pending is null)
        {
            _states.TryRemove(chatId, out _);
            return;
        }

        _states[chatId] = new SpatialChatLayerState(layers, pending);
    }
}
