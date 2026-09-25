using AskLucy.Application.Conversations.Capabilities;

namespace AskLucy.Application.Conversations.Runtime;

/// <summary>
/// What has already run for the site the conversation is on, projected from recorded turn
/// outcomes — the same outcome-derived source as <see cref="RecentTurnOutcomeSummary"/>, never
/// the prose.
///
/// <para>
/// <b>Why the offer step needs it.</b> The offer's own "don't suggest what just ran" rule only
/// sees the current turn. After Site Analysis it offered Sunlight; after Sunlight it offered Site
/// Analysis again, because that had run a turn earlier rather than this one — and a user clicking
/// through the offers could go round that loop forever.
/// </para>
///
/// <para>
/// <b>Where "this site" starts.</b> At the newest successful <c>resolve_location</c>: everything
/// that succeeded after it ran against the site now on screen. A conversation with no recorded
/// resolution has had one site throughout as far as the record knows, so the whole record counts.
/// Failed attempts do not count — a capability that failed is exactly what the user may still
/// want offered.
/// </para>
/// </summary>
public static class ActiveSiteHistory
{
    /// <param name="outcomesOldestFirst">Recorded outcomes in conversation order; nulls are skipped.</param>
    public static IReadOnlyList<string> CompletedCapabilityKeys(IEnumerable<RecordedTurnOutcome?> outcomesOldestFirst)
    {
        ArgumentNullException.ThrowIfNull(outcomesOldestFirst);

        var attempts = outcomesOldestFirst
            .Where(o => o is not null)
            .SelectMany(o => o!.Attempts)
            .Where(a => a.Succeeded && !string.IsNullOrEmpty(a.Key))
            .ToList();

        var siteStart = attempts.FindLastIndex(a => a.Key == ResolveLocationCapability.CapabilityKey) + 1;

        return [.. attempts
            .Skip(siteStart)
            .Select(a => a.Key!)
            .Distinct(StringComparer.Ordinal)];
    }
}
