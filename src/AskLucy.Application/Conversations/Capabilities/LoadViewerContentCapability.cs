using System.Text.Json;
using AskLucy.Application.Agents.Tools;
using AskLucy.Domain.Agents;

namespace AskLucy.Application.Conversations.Capabilities;

/// <summary>
/// Loads georeferenced 3D content into the viewer (specs/051-viewer-scene-content-api FR-001,
/// FR-004, research D8).
///
/// <para>
/// Emits a command rather than performing work, exactly like <see cref="AdjustViewerFocusCapability"/>:
/// the viewer is a browser-side surface, so this capability's output travels to the client via the
/// existing tool-result/SSE trailing-event channel (<c>StructuredPayloadExtractor</c>,
/// <c>AiController</c>'s <c>__VIEWER_CONTENT__</c> event), and the client performs the actual
/// <c>engine.loadContent(...)</c> call. No new dispatch mechanism is introduced.
/// </para>
///
/// <para>
/// Content is identified only by <c>fileId</c> — resolved through the platform's existing
/// signed-URL file access mechanism client-side (<c>gltfContentLoader.ts</c>), never an
/// externally-supplied URL (spec Assumptions). Loading/replacing/clearing is otherwise a viewer
/// command surface, not a panel-style push: there is no per-user delivery guarantee to build here
/// beyond what the SSE stream already provides for every other viewer command.
/// </para>
/// </summary>
public sealed class LoadViewerContentCapability : IConversationCapability
{
    public const string CapabilityKey = "load_viewer_content";

    public string Name => CapabilityKey;

    public string Description => "Loads a 3D model into the viewer at a real-world location.";

    public string WhenToUse =>
        "Use when the user asks to show, load or place a 3D model — a proposed building, a site " +
        "model — at a location, replacing or adding to whatever the viewer currently shows.";

    public string ArgumentHint => "fileId; latitude/longitude; optional height, orientation, scale";

    public string UsageGuidance =>
        "State briefly what is being loaded and where; the user will see it appear, so do not " +
        "narrate the load itself once it has started.";

    public string Label => "Load into the viewer";

    public string OfferDescription => "Show this as a 3D model in the viewer.";

    public string AcknowledgementTemplate => "Loading it into the viewer.";

    public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Low;

    public IReadOnlyList<AgentToolPermission> RequiredPermissions => [];

    public string InputSchemaJson =>
        """{"type":"object","required":["fileId","latitude","longitude"],"properties":{"fileId":{"type":"string","minLength":1},"latitude":{"type":"number","minimum":-90,"maximum":90},"longitude":{"type":"number","minimum":-180,"maximum":180},"heightMetres":{"type":"number"},"orientationDegrees":{"type":"number"},"scale":{"type":"number","exclusiveMinimum":0}}}""";

    public string OutputSchemaJson =>
        """{"type":"object","properties":{"fileId":{"type":"string"},"latitude":{"type":"number"},"longitude":{"type":"number"}}}""";

    public CapabilityDuration ExpectedDuration => CapabilityDuration.Brief;

    public SubAgentArea Area => SubAgentArea.Viewer;

    /// <summary>Always available — content loading has no viewer-state precondition, unlike
    /// zooming (which needs an active location) or panels (which have a concurrency cap).</summary>
    public bool IsAvailable(TurnContext context) => true;

    /// <summary>Never offered — loading a specific model is asked for directly, not suggested
    /// after the fact (mirrors <see cref="AdjustViewerFocusCapability"/>'s own reasoning).</summary>
    public bool IsOfferable(TurnContext context, TurnOutcome justCompleted) => false;

    public Task<AgentToolResult> ExecuteAsync(
        AgentToolExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
    {
        var root = input.RootElement;

        if (!root.TryGetProperty("fileId", out var fileIdElement) ||
            fileIdElement.GetString() is not { Length: > 0 } fileId)
        {
            return Task.FromResult(AgentToolResult.Failure("A fileId is required."));
        }

        if (!root.TryGetProperty("latitude", out var latitudeElement) || !latitudeElement.TryGetDouble(out var latitude) ||
            latitude is < -90 or > 90)
        {
            return Task.FromResult(AgentToolResult.Failure("A latitude between -90 and 90 is required."));
        }

        if (!root.TryGetProperty("longitude", out var longitudeElement) || !longitudeElement.TryGetDouble(out var longitude) ||
            longitude is < -180 or > 180)
        {
            return Task.FromResult(AgentToolResult.Failure("A longitude between -180 and 180 is required."));
        }

        var heightMetres = root.TryGetProperty("heightMetres", out var heightEl) && heightEl.TryGetDouble(out var h) ? h : 0d;
        var orientationDegrees = root.TryGetProperty("orientationDegrees", out var orientationEl) && orientationEl.TryGetDouble(out var o) ? o : 0d;
        var scale = root.TryGetProperty("scale", out var scaleEl) && scaleEl.TryGetDouble(out var s) && s > 0 ? s : 1d;

        return Task.FromResult(AgentToolResult.Success(JsonSerializer.SerializeToDocument(new
        {
            fileId,
            latitude,
            longitude,
            heightMetres,
            orientationDegrees,
            scale,
        })));
    }
}
