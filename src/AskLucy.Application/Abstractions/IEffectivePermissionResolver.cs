using AskLucy.Domain.Authorization;

namespace AskLucy.Application.Abstractions;

/// <summary>
/// Resolves a user's effective permission set right now (research.md Decision 3): built-in role
/// ⇒ the full catalogue (even permissions added after the role was granted); custom role ⇒ its
/// stored grants intersected with the current catalogue (retired keys drop out silently, Decision 10);
/// no role ⇒ empty.
/// </summary>
public interface IEffectivePermissionResolver
{
    Task<PermissionSet> ResolveAsync(string userId, CancellationToken cancellationToken = default);
}
