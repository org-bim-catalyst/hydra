namespace AskLucy.Domain.OperationalFailures;

/// <summary>
/// A distinct affected user or source address of an incident (research D10). It keeps the
/// distinct counts exact past the occurrence cap and through erasure, which rewrites
/// <see cref="ParticipantKey"/> to <c>erased:{guid}</c>.
///
/// Deliberately not a <see cref="Common.BaseEntity"/>: it is a child row keyed by
/// <c>(IncidentId, ParticipantType, ParticipantKey)</c>, inserted set-based with
/// <c>INSERT … WHERE NOT EXISTS</c> and never tracked, updated through the change tracker or
/// soft-deleted, so a surrogate id, row version and audit columns would never be used.
/// </summary>
public sealed class IncidentParticipant
{
    public Guid IncidentId { get; private set; }

    public IncidentParticipantType ParticipantType { get; private set; }

    public string ParticipantKey { get; private set; } = string.Empty;

    public DateTime FirstSeenUtc { get; private set; }

    private IncidentParticipant()
    {
        // Required by EF Core materialization.
    }
}
