using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Domain.Agents;

namespace AskLucy.Application.Conversations.Capabilities;

/// <summary>
/// specs/052-solar-analysis contracts/open-solar-analysis-capability.md — opens solar analysis
/// for the ACTIVE site and describes what it shows; performs NO solar computation itself
/// (research D3, FR-034). Mirrors the trailing-SSE tag pattern proven twice already —
/// <see cref="AdjustViewerFocusCapability"/>/<c>__ZOOM__</c> and
/// <see cref="LoadViewerContentCapability"/>/<c>__VIEWER_CONTENT__</c>.
///
/// <para>
/// <see cref="IsAvailable"/> is unconditionally true (unlike <see cref="AdjustViewerFocusCapability"/>,
/// which gates on <see cref="TurnContext.HasActiveLocation"/>) because the "no active site"
/// outcome must be something Lucy narrates in her own words (FR-035), not a capability the
/// catalog silently hides. The precondition is instead checked inside <see cref="ExecuteAsync"/>,
/// which reads the linked chat's own active location directly — <see cref="AgentToolExecutionContext"/>
/// carries no <see cref="TurnContext"/> for this to reuse.
/// </para>
/// </summary>
public sealed class OpenSolarAnalysisCapability(IUserChatRepository userChatRepository) : IConversationCapability
{
    public const string CapabilityKey = "open_solar_analysis";

    public string Name => CapabilityKey;

    public string Description => "Opens the sun-path and shadow analysis for the site currently shown in the viewer.";

    public string WhenToUse =>
        "Use when the user asks about sunlight, shadows, sun position, or how the site is oriented " +
        "to the sun — \"where does the sun go\", \"will the courtyard get sun in the afternoon\", " +
        "\"how far will that shadow reach\" — for the site currently shown in the viewer.";

    public string ArgumentHint => "optional date (YYYY-MM-DD) and time of day (HH:mm), both site-local";

    /// <summary>
    /// FR-033, FR-034 — this wording is the ONLY mechanism that makes FR-034 true, since this
    /// capability deliberately returns no solar figures (research D3): the figures are computed
    /// once, in the browser, and are already visible to the user in the panel this call opens.
    /// Asserted verbatim in <c>OpenSolarAnalysisCapabilityTests</c> so a later edit cannot quietly
    /// turn this into a figure-reciting prompt without a test failing.
    /// </summary>
    public string UsageGuidance =>
        "The analysis you just opened already shows the sun path, shadows and the numeric figures " +
        "on screen — describe what it shows qualitatively (how the site sits against the sun, where " +
        "shadows fall and how they move through the day) and do NOT recite the azimuth, altitude, " +
        "sunrise or sunset figures the panel already displays; the user can already see them.";

    public string Label => "Show sun & shadows";

    public string OfferDescription => "See how the sun and shadows move over this site.";

    public string AcknowledgementTemplate => "Opening the sun and shadow analysis.";

    public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Low;

    public IReadOnlyList<AgentToolPermission> RequiredPermissions => [];

    public string InputSchemaJson =>
        """{"type":"object","properties":{"date":{"type":"string","pattern":"^[0-9]{4}-[0-9]{2}-[0-9]{2}$"},"timeOfDay":{"type":"string","pattern":"^[0-9]{2}:[0-9]{2}$"}}}""";

    public string OutputSchemaJson =>
        """{"type":"object","properties":{"opened":{"type":"boolean"},"date":{"type":"string"},"timeOfDay":{"type":"string"},"reason":{"type":"string"}}}""";

    public CapabilityDuration ExpectedDuration => CapabilityDuration.Brief;

    public SubAgentArea Area => SubAgentArea.Viewer;

    /// <summary>Always true — see the class-level remarks on why the "no active site" outcome is
    /// produced by <see cref="ExecuteAsync"/> narration rather than catalog filtering (FR-035).</summary>
    public bool IsAvailable(TurnContext context) => true;

    /// <summary>Lucy invokes it directly when asked; it is not offered as a suggested action.</summary>
    public bool IsOfferable(TurnContext context, TurnOutcome justCompleted) => false;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
    {
        if (context.UserChatId is not { } userChatId)
        {
            return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new { opened = false, reason = "viewer-unavailable" }));
        }

        var chat = await userChatRepository.GetByIdAsync(userChatId, cancellationToken);
        if (chat?.ActiveLocation is null)
        {
            return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new { opened = false, reason = "no-active-site" }));
        }

        var root = input.RootElement;
        var date = root.TryGetProperty("date", out var dateEl) ? dateEl.GetString() ?? string.Empty : string.Empty;
        var timeOfDay = root.TryGetProperty("timeOfDay", out var timeEl) ? timeEl.GetString() ?? string.Empty : string.Empty;

        return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new { opened = true, date, timeOfDay }));
    }
}
