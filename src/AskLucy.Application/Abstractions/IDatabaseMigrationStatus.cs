namespace AskLucy.Application.Abstractions;

/// <summary>
/// Whether the database schema is fully migrated (constitution §3 Dependency Rule) — neither
/// <c>Application</c> nor <c>Infrastructure</c> may reference <c>AskLucyDbContext</c> directly, so
/// <see cref="AskLucy.Application.Conversations.SystemAgents.ISystemAgentProvisioner"/> reaches this through the same abstraction
/// boundary every other repository crosses, implemented in <c>AskLucy.Persistence</c>.
/// </summary>
public interface IDatabaseMigrationStatus
{
    Task<bool> HasPendingMigrationsAsync(CancellationToken cancellationToken = default);
}
