using System.ComponentModel.DataAnnotations;

namespace AskLucy.Application.Options;

/// <summary>
/// Per-subscription-tier storage quota (FR-011, spec.md Assumptions — "enforced according to
/// the user's existing subscription tier"). The Billing Engine's tier model isn't implemented
/// yet in this codebase, so these are flat defaults applied uniformly for now; wiring per-tier
/// values through is a configuration change, not an architecture change, once tiers exist.
/// </summary>
public sealed class DocumentStorageQuotaOptions
{
    public const string SectionName = "DocumentStorageQuota";

    /// <summary>Total bytes a single user may store across all their documents before uploads are blocked (FR-011, US6 AC4). Defaults to 10 GB.</summary>
    [Range(1, long.MaxValue)]
    public long DefaultQuotaBytes { get; init; } = 10L * 1024 * 1024 * 1024;
}
