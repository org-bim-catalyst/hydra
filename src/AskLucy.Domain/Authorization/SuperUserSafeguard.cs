using AskLucy.Domain.Common;

namespace AskLucy.Domain.Authorization;

/// <summary>
/// Pure domain service enforcing the "at least one active Super User" invariant.
/// Replaces the existing LastSuperUserGuard logic so that single-role, bulk-assign, and deletion paths share a single rule.
/// </summary>
public static class SuperUserSafeguard
{
    /// <summary>
    /// Ensures at least one active Super User remains after role removal operations.
    /// </summary>
    /// <param name="activeSuperUsersBefore">Count of active Super Users before the operation.</param>
    /// <param name="superUsersBeingRemoved">How many Super User assignments are being removed in this operation.</param>
    /// <exception cref="DomainRuleViolationException">Thrown when the result would leave zero active Super Users.</exception>
    public static void EnsureAtLeastOneActiveSuperUserRemains(int activeSuperUsersBefore, int superUsersBeingRemoved)
    {
        // Nothing is being removed — never this rule's business, regardless of the current
        // count (mirrors the original LastSuperUserGuard's early return when the action doesn't
        // touch Super User status at all).
        if (superUsersBeingRemoved <= 0)
        {
            return;
        }

        if (activeSuperUsersBefore - superUsersBeingRemoved < 1)
        {
            throw new DomainRuleViolationException(
                "At least one active Super User must remain after removing this role assignment.");
        }
    }
}
