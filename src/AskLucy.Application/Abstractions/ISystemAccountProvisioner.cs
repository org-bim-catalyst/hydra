namespace AskLucy.Application.Abstractions;

/// <summary>
/// Ensures the platform's own pseudo-user account exists — the row <see cref="AskLucy.Domain.Agents.Agent.SystemOwnerId"/>
/// ("system") refers to wherever an owner id must resolve to a real account (e.g. the
/// <c>Agents.OwnerId</c> foreign key). Idempotent: safe to call on every startup.
/// </summary>
public interface ISystemAccountProvisioner
{
    Task EnsureSystemAccountExistsAsync(CancellationToken cancellationToken = default);
}
