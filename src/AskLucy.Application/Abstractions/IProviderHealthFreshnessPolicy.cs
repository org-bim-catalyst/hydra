namespace AskLucy.Application.Abstractions;

/// <summary>
/// specs/043 FR-019 — how long a recorded provider-health result may be presented as current
/// fact before it must be shown as possibly out of date.
///
/// The window is deliberately expressed as a multiple of the configured background-check
/// interval rather than an absolute duration: hard-coding, say, six minutes would mark every
/// provider permanently stale the moment someone widened the interval past it.
///
/// Exists as an Application-owned abstraction because the interval itself lives in
/// Infrastructure configuration, which Application may not read (constitution §3).
/// </summary>
public interface IProviderHealthFreshnessPolicy
{
    /// <summary>
    /// The instant after which a result recorded at <paramref name="checkedAtUtc"/> should be
    /// presented as possibly out of date. Returns <c>null</c> when no check has run, since
    /// "never checked" is a distinct state from "checked and stale" (FR-020).
    /// </summary>
    DateTime? StaleAfterUtc(DateTime? checkedAtUtc);
}
