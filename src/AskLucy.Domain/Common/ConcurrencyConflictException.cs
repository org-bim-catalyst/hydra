namespace AskLucy.Domain.Common;

/// <summary>
/// A write was rejected because the resource had already changed since the caller last read it
/// (optimistic concurrency). Distinct from <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>
/// (an Infrastructure/EF Core type Application/Domain must never reference, constitution §3) —
/// this is the Application-layer-safe equivalent, mapped to the same 409 Problem Details.
/// </summary>
public sealed class ConcurrencyConflictException(string message, string? currentRevision = null) : Exception(message)
{
    /// <summary>
    /// specs/079 — the revision now in force, when the caller can use it to reload (the site
    /// outline editor). Surfaced as a <c>currentRevision</c> Problem Details extension.
    /// </summary>
    public string? CurrentRevision { get; } = currentRevision;
}
