using System.Text.Json;
using AskLucy.Application.Conversations.Capabilities;

namespace AskLucy.Application.Conversations.Flows;

/// <summary>
/// Finds a named place and outlines its site boundary — one job, two steps (specs/045 FR-051,
/// contracts/capability-flow.md).
///
/// <para>
/// <b>The former step 2 is gone (2026-09-11), not just skipped.</b> The contract's step 2 was
/// "focus the viewer on the newly found place," bound to <see cref="AdjustViewerFocusCapability"/>
/// with a hardcoded <c>direction: "in"</c> — a deliberate interpretation from a time when the
/// viewer's own automatic recentre-on-<c>__LOCATION__</c> (<c>ViewerSurface.tsx</c>) fell back to
/// a wide, fixed default zoom whenever the geocoder's viewport/locationType data was silently
/// lost in transit (a bug fixed the same day as this one — see <c>ResolveLocationCapability</c>'s
/// remarks). The extra forced zoom-in compensated for that loss. Once the viewport/locationType
/// fix landed, step 1's own auto-zoom started framing the site correctly on its own — and this
/// step's zoom-in then stacked on top of an already-correct frame, live-tested as "zoomed twice"
/// and "too tight to see the boundary while rotating." Removing it, rather than special-casing it
/// away, is correct: nothing about "found a place" implies "and also zoom in one more stop" once
/// the thing it was compensating for no longer happens. <see cref="AdjustViewerFocusCapability"/>
/// itself is untouched and still reachable directly for an explicit "zoom in"/"zoom out" request —
/// only this flow's own forced invocation of it is gone.
/// </para>
/// </summary>
public sealed class LocateAPlaceFlow : IConversationFlow
{
    public const string FlowKey = "locate_a_place";

    public string Key => FlowKey;

    public string Description =>
        "Finds a named real-world place, focuses the map viewer on it, and outlines the site " +
        "boundary — one job, two steps.";

    public string WhenToUse =>
        "Use when the user asks to see, find, locate or navigate to a named place, site, park, " +
        "building or address they want shown on the map.";

    public string ArgumentHint => "query: the place name";

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
            CapabilityKey: ResolveSiteBoundaryCapability.CapabilityKey,
            AnnouncementTemplate: "Now highlighting the boundary.",
            BindArguments: BindBoundaryArguments,
            IsAlreadySatisfied: ctx => IsSamePlace(ResolvedLocationName(ctx), ctx.Turn.ActiveBoundary?.SiteName),
            CompletionTemplate: "Boundary highlighted",
            SkipTemplate: "The site is already outlined, so I've left it as it is."),
    ];

    public IReadOnlyList<FlowVariant> Variants { get; } =
    [
        new FlowVariant("focus", "Focus the viewer on it", "Find it and centre the map on it.", ThroughStepIndex: 0),
        new FlowVariant("full", "Focus and outline the site", "Find it, centre the map, and outline the site boundary.", ThroughStepIndex: 1),
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
