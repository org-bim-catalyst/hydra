using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Panels;
using AskLucy.Domain.Agents;

namespace AskLucy.Application.Conversations.Capabilities;

/// <summary>
/// Presents a result as a floating panel over the viewer (specs/045 FR-013, reusing the specs/028
/// panel framework).
///
/// <para>
/// The panel-type registry lives in the browser, so the set of renderable types is a client fact
/// this capability cannot enumerate. It therefore validates against the four built-in types the
/// registry ships, which is the honest boundary: proposing a type the client cannot render would
/// surface as an error panel, and an offer that produces an error is exactly the ungrounded
/// suggestion the whole grounding design exists to prevent.
/// </para>
///
/// <para>
/// specs/028 was built so that "Ask Lucy can call the viewer" — and then never wired to an agent.
/// This is that wiring.
/// </para>
/// </summary>
public sealed class OpenVisualPanelCapability(IPanelNotifier panelNotifier) : IConversationCapability
{
    public const string CapabilityKey = "open_visual_panel";

    /// <summary>
    /// The types <c>viewer/panels/types/index.ts</c> registers. Duplicated here rather than
    /// discovered because the registry is client-side; kept to a named constant so the drift is
    /// visible in one place if a fifth type is added.
    /// </summary>
    private static readonly HashSet<string> RenderableTypeKeys =
        new(StringComparer.Ordinal) { "chart", "table", "parameters", "summary" };

    /// <summary>Clarifications Q3 (specs/028) — beyond this the least-recently-focused panel is closed to make room.</summary>
    private const int MaxConcurrentPanels = 6;

    public string Name => CapabilityKey;

    public string Description =>
        "Shows a result as a floating panel over the viewer — a chart, a table, a parameter list or a summary.";

    public string WhenToUse =>
        "Use when the answer is better seen than read: comparisons, breakdowns, tabular figures, " +
        "or a summary the user will want to keep on screen while looking at the map.";

    public string ArgumentHint => "typeKey: chart/table/parameters/summary; title: label; data: content object";

    public string UsageGuidance =>
        "Pick the type that fits the shape of the data, not the one that looks most impressive: a " +
        "chart for trends, a table for figures to compare row by row, parameters for a labelled " +
        "list, a summary for prose. Say in one line what the panel shows; do not restate its " +
        "contents in the reply, because the user can see them.";

    public string Label => "Show it as a panel";

    public string OfferDescription => "Open this as a floating panel over the map.";

    public string AcknowledgementTemplate => "Opening a panel for it.";

    public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Low;

    public IReadOnlyList<AgentToolPermission> RequiredPermissions => [];

    public string InputSchemaJson =>
        """{"type":"object","required":["typeKey","title","data"],"properties":{"typeKey":{"type":"string","enum":["chart","table","parameters","summary"]},"title":{"type":"string","minLength":1},"data":{"type":"object"}}}""";

    public string OutputSchemaJson =>
        """{"type":"object","properties":{"requestId":{"type":"string"},"typeKey":{"type":"string"}}}""";

    public CapabilityDuration ExpectedDuration => CapabilityDuration.Brief;

    public SubAgentArea Area => SubAgentArea.Viewer;

    /// <summary>Available while there is room; past the cap the framework evicts, but Lucy should not be the one forcing that.</summary>
    public bool IsAvailable(TurnContext context) => context.OpenPanelTypeKeys.Count < MaxConcurrentPanels;

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
        if (!root.TryGetProperty("typeKey", out var typeElement) ||
            typeElement.GetString() is not { Length: > 0 } typeKey)
        {
            return AgentToolResult.Failure("A panel type is required.");
        }

        if (!RenderableTypeKeys.Contains(typeKey))
        {
            return AgentToolResult.Failure(
                $"'{typeKey}' is not a registered panel type. Registered types: {string.Join(", ", RenderableTypeKeys)}.");
        }

        if (!root.TryGetProperty("title", out var titleElement) ||
            titleElement.GetString() is not { Length: > 0 } title)
        {
            return AgentToolResult.Failure("A panel title is required.");
        }

        if (!root.TryGetProperty("data", out var dataElement))
        {
            return AgentToolResult.Failure("Panel data is required.");
        }

        var requestId = Guid.CreateVersion7().ToString();

        try
        {
            await panelNotifier.PanelRequestedAsync(
                context.UserId,
                new PanelRequestDto(requestId, typeKey, title, dataElement.Clone(), Position: null, ContextAssociation: null),
                cancellationToken);

            return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new { requestId, typeKey }));
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
