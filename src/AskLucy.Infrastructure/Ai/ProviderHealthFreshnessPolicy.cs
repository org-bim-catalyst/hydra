using AskLucy.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Ai;

/// <summary>
/// specs/043 FR-019, research.md Decision 6 — implements the freshness window over the same
/// <see cref="ProviderHealthCheckOptions.Interval"/> that <see cref="ProviderHealthCheckHostedService"/>
/// runs on, so the two can never drift apart.
/// </summary>
public sealed class ProviderHealthFreshnessPolicy(IOptions<ProviderHealthCheckOptions> options) : IProviderHealthFreshnessPolicy
{
    /// <summary>
    /// Three missed cycles is a genuine signal the checker has stopped rather than a blip —
    /// tight enough to reveal a stopped background service within minutes, loose enough that
    /// one slow cycle or transient database hiccup does not flag every provider.
    /// </summary>
    private const int IntervalMultiplier = 3;

    public DateTime? StaleAfterUtc(DateTime? checkedAtUtc) =>
        checkedAtUtc is { } checkedAt
            ? checkedAt + (options.Value.Interval * IntervalMultiplier)
            : null;
}
