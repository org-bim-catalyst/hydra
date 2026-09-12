using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Panels;
using AskLucy.Domain.Agents;

namespace AskLucy.Application.Conversations.Capabilities;

/// <summary>
/// Presents a result as a floating panel over the viewer, composed freely from the content
/// vocabulary (specs/049 FR-001-FR-004, contracts/content-vocabulary.md).
///
/// <para>
/// Replaces <c>OpenVisualPanelCapability</c>'s four-type hardcoded list. This capability is
/// always available and never names a type: Lucy composes an ordered sequence of content blocks
/// (heading, text, keyValue, table, chart, metric, image, divider) and this capability pushes it
/// as-is. <see cref="InputSchemaJson"/> declares the vocabulary's document envelope — the exact
/// same schema <c>contracts/panel-content.schema.json</c> is generated to, which
/// <c>viewer/panels/content/blocks.schema.test.ts</c> keeps in step with the client's zod source
/// (specs/049 research D1/D2). <c>PresentPanelContentCapabilityTests.InputSchemaJson_ContentSubschema_ShouldMatchTheCommittedVocabularyArtifact</c>
/// keeps this literal in step with that same committed artifact.
/// </para>
///
/// <para>
/// The envelope is deliberately loose at the block level — it only requires each block to be an
/// object carrying a <c>kind</c> string, not that it matches one of the eight shapes exactly.
/// Individual block validity is the client's job, one block at a time, at render
/// (<c>ContentRenderer.tsx</c>) — a single malformed or unrecognised block must show a visible
/// placeholder while every sibling still renders (spec User Story 4), which a fully strict
/// whole-document schema would prevent by rejecting the entire composition.
/// </para>
/// </summary>
public sealed class PresentPanelContentCapability(IPanelNotifier panelNotifier) : IConversationCapability
{
    public const string CapabilityKey = "present_panel_content";

    /// <summary>Clarifications Q3 (specs/028) — beyond this the least-recently-focused panel is closed to make room.</summary>
    private const int MaxConcurrentPanels = 6;

    public string Name => CapabilityKey;

    public string Description =>
        "Shows a result as a floating panel over the viewer, composed freely from headings, text, " +
        "labelled values, tables, charts, a highlighted figure, an image or a separator.";

    public string WhenToUse =>
        "Use when the answer is better seen than read: a place's details, a comparison, a " +
        "breakdown of figures, or a summary the user will want to keep on screen while looking " +
        "at the map.";

    public string ArgumentHint => "title: label; content: blocks composed from the vocabulary";

    public string UsageGuidance =>
        "Compose the blocks that fit the shape of what you found — a heading and keyValue block " +
        "for a place's details, a table for figures to compare row by row, a chart for a trend, " +
        "prose for a summary. A table row, a keyValue item or a metric may carry an action so the " +
        "user can act on it directly. Say in one line what the panel shows; do not restate its " +
        "contents in the reply, because the user can see them (spec FR-029).";

    public string Label => "Show it as a panel";

    public string OfferDescription => "Open this as a floating panel over the map.";

    public string AcknowledgementTemplate => "Opening a panel for it.";

    public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Low;

    public IReadOnlyList<AgentToolPermission> RequiredPermissions => [];

    public string InputSchemaJson =>
        """{"type":"object","required":["title","content"],"properties":{"title":{"type":"string","minLength":1},"content":{"type":"object","properties":{"version":{"type":"number","const":1},"blocks":{"minItems":1,"maxItems":50,"type":"array","items":{"type":"object","properties":{"kind":{"type":"string"}},"required":["kind"],"additionalProperties":{}}}},"required":["version","blocks"],"additionalProperties":false}}}""";

    public string OutputSchemaJson =>
        """{"type":"object","properties":{"requestId":{"type":"string"}}}""";

    public CapabilityDuration ExpectedDuration => CapabilityDuration.Brief;

    public SubAgentArea Area => SubAgentArea.Viewer;

    /// <summary>Available while there is room; past the cap the framework evicts, but Lucy should not be the one forcing that.</summary>
    public bool IsAvailable(TurnContext context) => context.OpenPanelCount < MaxConcurrentPanels;

    /// <summary>
    /// Offerable only when the turn actually produced something a panel would render. Suggesting a
    /// panel with nothing to put in it is the emptiest kind of offer.
    /// </summary>
    public bool IsOfferable(TurnContext context, TurnOutcome justCompleted) =>
        IsAvailable(context) &&
        !justCompleted.WasInvokedThisTurn(CapabilityKey) &&
        justCompleted.InvokedCapabilityKeys.Count > 0;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
    {
        var root = input.RootElement;

        if (!root.TryGetProperty("title", out var titleElement) ||
            titleElement.GetString() is not { Length: > 0 } title)
        {
            return AgentToolResult.Failure("A panel title is required.");
        }

        if (!root.TryGetProperty("content", out var contentElement))
        {
            return AgentToolResult.Failure("Panel content is required.");
        }

        var requestId = Guid.CreateVersion7().ToString();

        try
        {
            await panelNotifier.PanelRequestedAsync(
                context.UserId,
                PanelRequestDto.ForContent(requestId, title, contentElement.Clone()),
                cancellationToken);

            return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new { requestId }));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return AgentToolResult.Failure($"The panel could not be opened: {ex.Message}");
        }
    }
}
