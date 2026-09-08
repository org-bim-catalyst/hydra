using System.Text.Json;
using AskLucy.Application.Agents.Tools;
using AskLucy.Domain.Agents;

namespace AskLucy.Application.Conversations.Capabilities;

/// <summary>
/// Adjusts how tightly the viewer frames the active location (specs/045 FR-013, FR-047).
///
/// <para>
/// Replaces <c>ViewerZoomDetector</c>, the keyword matcher from specs/038 that scanned every
/// message for "zoom in", "closer", "pull back" and a dozen more. That approach could not tell a
/// request from a mention and had no notion of whether zooming was even possible; making it a
/// capability puts the decision with the model that already read the sentence, and puts the
/// precondition somewhere it can be checked.
/// </para>
///
/// <para>
/// Emits a command rather than performing work: the viewer is a browser-side surface, so this
/// capability's output travels to the client, which does the moving. That is why it is
/// <see cref="CapabilityDuration.Brief"/> — there is no network call here at all.
/// </para>
/// </summary>
public sealed class AdjustViewerFocusCapability : IConversationCapability
{
    public const string CapabilityKey = "adjust_viewer_focus";

    public string Name => CapabilityKey;

    public string Description => "Zooms the map viewer in or out on the location currently shown.";

    public string WhenToUse =>
        "Use when the user asks to zoom, get closer, pull back, see more context, or otherwise " +
        "change how tightly the map frames the place already on screen.";

    public string ArgumentHint => "the direction: in or out";

    public string UsageGuidance =>
        "Confirm the change briefly and stop — never claim you are unable to control the viewer, " +
        "because you are. There is nothing to report beyond the direction: the user can see the " +
        "result themselves, so describing it back to them is noise.";

    public string Label => "Zoom the viewer";

    public string OfferDescription => "Change how closely the map frames this place.";

    public string AcknowledgementTemplate => "Now focusing the viewer on it.";

    public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Low;

    public IReadOnlyList<AgentToolPermission> RequiredPermissions => [];

    public string InputSchemaJson =>
        """{"type":"object","required":["direction"],"properties":{"direction":{"type":"string","enum":["in","out"]}}}""";

    public string OutputSchemaJson => """{"type":"object","properties":{"direction":{"type":"string"}}}""";

    public CapabilityDuration ExpectedDuration => CapabilityDuration.Brief;

    /// <summary>Nothing to zoom without a place on screen — the split-brain guard specs/038 added after the fact.</summary>
    public bool IsAvailable(TurnContext context) => context.HasActiveLocation;

    /// <summary>
    /// Never offered. Zooming is asked for directly and takes no time; a row for it would be one
    /// nobody picks, crowding out suggestions that are actually worth making.
    /// </summary>
    public bool IsOfferable(TurnContext context, TurnOutcome justCompleted) => false;

    public Task<AgentToolResult> ExecuteAsync(
        AgentToolExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
    {
        if (!input.RootElement.TryGetProperty("direction", out var directionElement) ||
            directionElement.GetString() is not { } direction ||
            (direction != "in" && direction != "out"))
        {
            return Task.FromResult(AgentToolResult.Failure("A direction of 'in' or 'out' is required."));
        }

        return Task.FromResult(AgentToolResult.Success(
            JsonSerializer.SerializeToDocument(new { direction })));
    }
}
