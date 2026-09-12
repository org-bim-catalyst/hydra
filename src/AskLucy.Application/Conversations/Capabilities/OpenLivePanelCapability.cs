using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Panels;
using AskLucy.Domain.Agents;

namespace AskLucy.Application.Conversations.Capabilities;

/// <summary>
/// Opens a named "live" panel — one whose content is code rather than data, because it holds
/// continuous state, owns its own drawing surface, or needs values flowing back into it live
/// (specs/049 FR-022, FR-024, FR-026, contracts/panel-request.md).
///
/// <para>
/// Distinct from <see cref="PresentPanelContentCapability"/> by design: presenting composed
/// content is always available to Lucy; opening a live panel is available only while something
/// has registered that kind. The registry that decides "what is registered" lives entirely in the
/// browser (specs/028's client-side <c>panelTypeRegistry</c>, narrowed by specs/049 to hold only
/// live kinds), and this feature introduces no server-side mechanism for the server to learn what
/// the client currently has registered — that would mean reintroducing the hardcoded type list
/// this whole feature exists to remove, just one layer further down. <see cref="IsAvailable"/> is
/// therefore honestly <c>false</c> here: no live panel kind ships in specs/049. specs/050 (the
/// viewer extension framework) supplies both the first live kinds and the reporting that would
/// make this capability's availability answerable.
/// </para>
/// </summary>
public sealed class OpenLivePanelCapability(IPanelNotifier panelNotifier) : IConversationCapability
{
    public const string CapabilityKey = "open_live_panel";

    public string Name => CapabilityKey;

    public string Description => "Opens a live, interactive panel — one that isn't just showing composed content.";

    public string WhenToUse =>
        "Use when the user asks to see a specific interactive tool panel that is available for " +
        "this session; not for presenting a result, which is present_panel_content's job.";

    public string ArgumentHint => "typeKey: the registered live panel kind; title: label; data: content object";

    public string UsageGuidance =>
        "Only invoke this with a typeKey you were told is available this turn — there is no " +
        "general-purpose live panel kind to guess at.";

    public string Label => "Open panel";

    public string OfferDescription => "Open this interactive panel.";

    public string AcknowledgementTemplate => "Opening it.";

    public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Low;

    public IReadOnlyList<AgentToolPermission> RequiredPermissions => [];

    public string InputSchemaJson =>
        """{"type":"object","required":["typeKey","title","data"],"properties":{"typeKey":{"type":"string","minLength":1},"title":{"type":"string","minLength":1},"data":{"type":"object"}}}""";

    public string OutputSchemaJson =>
        """{"type":"object","properties":{"requestId":{"type":"string"},"typeKey":{"type":"string"}}}""";

    public CapabilityDuration ExpectedDuration => CapabilityDuration.Brief;

    public SubAgentArea Area => SubAgentArea.Viewer;

    /// <summary>
    /// Honestly <c>false</c>: no live panel kind ships in specs/049, and this feature introduces no
    /// server-side registry of what the client currently has registered (see class remarks).
    /// specs/050 gives this a real answer.
    /// </summary>
    public bool IsAvailable(TurnContext context) => false;

    public bool IsOfferable(TurnContext context, TurnOutcome justCompleted) => false;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
    {
        var root = input.RootElement;

        if (!root.TryGetProperty("typeKey", out var typeKeyElement) ||
            typeKeyElement.GetString() is not { Length: > 0 } typeKey)
        {
            return AgentToolResult.Failure("A panel type is required.");
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
                PanelRequestDto.ForLive(requestId, title, typeKey, dataElement.Clone()),
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
