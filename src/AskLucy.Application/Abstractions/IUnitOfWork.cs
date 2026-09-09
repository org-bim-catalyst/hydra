namespace AskLucy.Application.Abstractions;

/// <summary>
/// A single business transaction commits through exactly one <see cref="SaveChangesAsync"/>
/// call, per constitution &#167;3 (Repository &amp; Unit of Work rules).
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Like <see cref="SaveChangesAsync"/>, but a uniqueness-constraint violation on
    /// <paramref name="uniqueIndexNameOnConflict"/> is absorbed into a <see langword="false"/>
    /// result — with every tracked change discarded — rather than thrown.
    /// <para>
    /// specs/045 FR-033's concurrent-provisioner recovery is the one caller
    /// (contracts/system-agent-provisioning.md §2 — "the loser catches the uniqueness violation,
    /// re-reads, and continues"): the SQL-specific detection this needs must live where EF Core
    /// and the SQL Server provider are already referenced (here, not in <c>Infrastructure</c>,
    /// which stays free of both per constitution §3), and discarding tracked changes on failure is
    /// what lets the caller's very next call — for an unrelated definition sharing this same
    /// scoped context — start clean instead of retrying a doomed pending insert.
    /// </para>
    /// </summary>
    Task<bool> TrySaveChangesAsync(string uniqueIndexNameOnConflict, CancellationToken cancellationToken = default);
}
