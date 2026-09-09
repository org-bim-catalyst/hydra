using System.Text.Json;
using AskLucy.Application.Conversations.Capabilities;

namespace AskLucy.Application.Conversations.Flows;

/// <summary>
/// Finds a named place, focuses the viewer on it, and outlines its site boundary — one job,
/// three steps (specs/045 FR-051, contracts/capability-flow.md).
///
/// <para>
/// <b>Step 2's capability choice.</b> The contract describes step 2 as "focus the viewer on the
/// newly found place." The only capability that exists for viewer framing is
/// <see cref="AdjustViewerFocusCapability"/> (specs/038), whose sole lever is zoom
/// direction — it has no "centre on these coordinates" mode, because the viewer already
/// recentres itself as soon as step 1's <c>__LOCATION__</c> payload reaches the client
/// (<c>TryExtractStructuredPayload</c> in <c>ConversationTurnOrchestrator</c>). Binding this step
/// to <c>direction: "in"</c> is a deliberate, documented interpretation — "found it, now focus
/// in on it" — rather than a literal step-for-step match to the contract's illustrative capability
/// name; building a second, coordinate-based viewer capability solely for this one flow step was
/// judged out of scope for specs/045.
/// </para>
/// </summary>
public sealed class LocateAPlaceFlow : IConversationFlow
{
    public const string FlowKey = "locate_a_place";

    public string Key => FlowKey;

    public string Description =>
        "Finds a named real-world place, focuses the map viewer on it, and outlines the site " +
        "boundary — one job, three steps.";

    public string WhenToUse =>
        "Use when the user asks to see, find, locate or navigate to a named place, site, park, " +
        "building or address they want shown on the map.";

    public string ArgumentHint => "the place name";

    public bool IsAvailable(TurnContext context) => true;

    public IReadOnlyList<FlowStep> Steps { get; } =
    [
        new FlowStep(
            CapabilityKey: ResolveLocationCapability.CapabilityKey,
            AnnouncementTemplate: "Looking for it.",
            BindArguments: ctx => ctx.FlowInput,
            IsAlreadySatisfied: _ => false,
            CompletionTemplate: "Location found"),

        new FlowStep(
            CapabilityKey: AdjustViewerFocusCapability.CapabilityKey,
            AnnouncementTemplate: "Now focusing the viewer on it.",
            BindArguments: _ => JsonSerializer.SerializeToDocument(new { direction = "in" }),
            IsAlreadySatisfied: ctx => IsSamePlace(ResolvedLocationName(ctx), ctx.Turn.ActiveLocation?.LocationName),
            CompletionTemplate: "Site focused",
            SkipTemplate: "The viewer is already focused on it, so I've left it as it is."),

        new FlowStep(
            CapabilityKey: ResolveSiteBoundaryCapability.CapabilityKey,
            AnnouncementTemplate: "Now highlighting the boundary.",
            BindArguments: BindBoundaryArguments,
            IsAlreadySatisfied: ctx => IsSamePlace(ResolvedLocationName(ctx), ctx.Turn.ActiveBoundary?.SiteName),
            CompletionTemplate: "Boundary highlighted",
            SkipTemplate: "The site is already outlined, so I've left it as it is."),
    ];

    public IReadOnlyList<FlowVariant> Variants { get; } =
    [
        new FlowVariant("focus", "Focus the viewer on it", "Find it and centre the map on it.", ThroughStepIndex: 1),
        new FlowVariant("full", "Focus and outline the site", "Find it, centre the map, and outline the site boundary.", ThroughStepIndex: 2),
    ];

    private static JsonDocument? BindBoundaryArguments(FlowStepContext ctx)
    {
        var step1 = ctx.CompletedSteps.FirstOrDefault(s => s.CapabilityKey == ResolveLocationCapability.CapabilityKey);
        if (step1 is not { Succeeded: true, ResultJson: { } json })
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return JsonSerializer.SerializeToDocument(new
        {
            latitude = root.GetProperty("latitude").GetDouble(),
            longitude = root.GetProperty("longitude").GetDouble(),
            locationName = root.GetProperty("locationName").GetString(),
            confidence = root.TryGetProperty("confidence", out var confEl) ? confEl.GetDouble() : 1d,
        });
    }

    /// <summary>The place step 1 actually resolved this run, read back from its own result — not the turn's stale snapshot.</summary>
    private static string? ResolvedLocationName(FlowStepContext ctx)
    {
        var step1 = ctx.CompletedSteps.FirstOrDefault(s => s.CapabilityKey == ResolveLocationCapability.CapabilityKey);
        if (step1 is not { Succeeded: true, ResultJson: { } json })
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("locationName", out var nameEl) ? nameEl.GetString() : null;
    }

    /// <summary>
    /// FR-057's non-redundancy check for steps 2/3: is the place step 1 just resolved the exact
    /// one <paramref name="activeName"/> already names? Comparing against the resolved name
    /// (not just "is a location already active") is what correctly lets a genuinely new place
    /// still run steps 2/3 even when the turn started with some other place already on screen.
    /// </summary>
    private static bool IsSamePlace(string? resolvedName, string? activeName) =>
        resolvedName is not null && activeName is not null &&
        string.Equals(resolvedName, activeName, StringComparison.OrdinalIgnoreCase);
}
