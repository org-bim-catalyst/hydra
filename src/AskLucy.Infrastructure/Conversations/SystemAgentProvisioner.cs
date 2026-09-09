using AskLucy.Application.Abstractions;
using AskLucy.Application.Conversations.SystemAgents;
using AskLucy.Domain.Agents;
using Microsoft.Extensions.Logging;

namespace AskLucy.Infrastructure.Conversations;

internal static partial class SystemAgentProvisionerLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "System agent provisioning deferred: the database has pending migrations")]
    public static partial void DeferredPendingMigrations(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "System agent provisioning deferred: the database was unreachable")]
    public static partial void DeferredUnreachable(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "System agent '{SystemKey}' created")]
    public static partial void Created(ILogger logger, string systemKey);

    [LoggerMessage(Level = LogLevel.Information, Message = "System agent '{SystemKey}' upgraded to version {VersionNumber}")]
    public static partial void Upgraded(ILogger logger, string systemKey, int versionNumber);

    [LoggerMessage(Level = LogLevel.Warning, Message = "System agent '{SystemKey}' lost a concurrent creation race; re-read the winning row")]
    public static partial void ConcurrentCreationLost(ILogger logger, string systemKey);

    [LoggerMessage(Level = LogLevel.Error, Message = "Provisioning system agent '{SystemKey}' failed; the turn continues with the other definitions")]
    public static partial void DefinitionFailed(ILogger logger, string systemKey, Exception exception);
}

/// <summary>
/// Upserts <see cref="SystemAgentDefinitions.All"/> into the agent catalog
/// (contracts/system-agent-provisioning.md §2). Driven by
/// <see cref="SystemAgentProvisioningHostedService"/> at startup.
/// </summary>
public sealed class SystemAgentProvisioner(
    IAgentRepository agentRepository,
    IUnitOfWork unitOfWork,
    IDatabaseMigrationStatus migrationStatus,
    ISystemAccountProvisioner systemAccountProvisioner,
    ILogger<SystemAgentProvisioner> logger) : ISystemAgentProvisioner
{
    private const string Actor = "system:provisioner";
    private const string SystemKeyUniqueIndexName = "IX_Agents_SystemKey";

    public async Task<SystemAgentProvisioningResult> ProvisionAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (await migrationStatus.HasPendingMigrationsAsync(cancellationToken))
            {
                SystemAgentProvisionerLog.DeferredPendingMigrations(logger);
                return SystemAgentProvisioningResult.DeferredResult;
            }

            // Every definition below is created with OwnerId = Agent.SystemOwnerId, which the
            // Agents.OwnerId foreign key requires to resolve to a real account row — this must
            // exist before the first ProvisionOneAsync call, not be discovered missing by it.
            await systemAccountProvisioner.EnsureSystemAccountExistsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Unreachable database — the same "log and defer, never throw" posture as a pending
            // migration (contracts/system-agent-provisioning.md §2). The host still starts; the
            // next startup retries.
            SystemAgentProvisionerLog.DeferredUnreachable(logger, ex);
            return SystemAgentProvisioningResult.DeferredResult;
        }

        var created = 0;
        var upgraded = 0;
        var unchanged = 0;

        foreach (var definition in SystemAgentDefinitions.All)
        {
            try
            {
                switch (await ProvisionOneAsync(definition, cancellationToken))
                {
                    case ProvisionOutcome.Created:
                        created++;
                        break;
                    case ProvisionOutcome.Upgraded:
                        upgraded++;
                        break;
                    case ProvisionOutcome.Unchanged:
                        unchanged++;
                        break;
                }
            }
            catch (Exception ex)
            {
                // One definition's failure must not take the rest of the pass down with it, nor
                // the host (constitution §2.VIII) — logged, and the loop continues.
                SystemAgentProvisionerLog.DefinitionFailed(logger, definition.SystemKey, ex);
            }
        }

        return new SystemAgentProvisioningResult(created, upgraded, unchanged, Deferred: false);
    }

    private enum ProvisionOutcome
    {
        Created,
        Upgraded,
        Unchanged,
    }

    private async Task<ProvisionOutcome> ProvisionOneAsync(SystemAgentDefinition definition, CancellationToken cancellationToken)
    {
        var hash = definition.ComputeHash();
        var agent = await agentRepository.GetBySystemKeyAsync(definition.SystemKey, cancellationToken);

        if (agent is null)
        {
            var candidate = Agent.CreateSystemProvisioned(
                definition.SystemKey, definition.Name, definition.Description, definition.AgentType,
                definition.Instructions, definition.ModelCapability, definition.ExecutionPolicy, Actor);
            agentRepository.Add(candidate);
            candidate.PublishSystemVersion(definition.CapabilityKeys, hash, Actor);

            if (await unitOfWork.TrySaveChangesAsync(SystemKeyUniqueIndexName, cancellationToken))
            {
                SystemAgentProvisionerLog.Created(logger, definition.SystemKey);
                return ProvisionOutcome.Created;
            }

            // Another instance won the race between our read and our write. Re-read what it
            // created and fall through to the ordinary hash-compare path below — from here on
            // this is indistinguishable from having found the agent already present.
            SystemAgentProvisionerLog.ConcurrentCreationLost(logger, definition.SystemKey);
            agent = await agentRepository.GetBySystemKeyAsync(definition.SystemKey, cancellationToken)
                ?? throw new InvalidOperationException($"System agent '{definition.SystemKey}' vanished after losing a creation race.");
        }

        var newestHash = agent.Versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault()?.DefinitionHash;
        if (string.Equals(newestHash, hash, StringComparison.Ordinal))
        {
            return ProvisionOutcome.Unchanged;
        }

        agent.UpdateSystemDefinition(definition.Name, definition.Description, definition.Instructions, definition.ModelCapability, definition.ExecutionPolicy, Actor);
        var version = agent.PublishSystemVersion(definition.CapabilityKeys, hash, Actor);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        SystemAgentProvisionerLog.Upgraded(logger, definition.SystemKey, version.VersionNumber);
        return ProvisionOutcome.Upgraded;
    }
}
