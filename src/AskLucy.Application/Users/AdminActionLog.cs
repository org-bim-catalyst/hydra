using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Users;

/// <summary>
/// Shared structured security-event log for every admin user-management action (FR-021) —
/// named properties per constitution &#167;4, not a new audit-trail store (spec.md Non-Goals;
/// see plan.md &#167; Complexity Tracking for the accepted interim scope of this logging).
/// </summary>
internal static partial class AdminActionLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Admin action {Action} performed by {ActorUserId} against {TargetUserId}: {Detail}")]
    public static partial void AdminUserActionPerformed(ILogger logger, string action, string actorUserId, string targetUserId, string detail);

    [LoggerMessage(Level = LogLevel.Information, Message = "Role change performed by {ActorUserId} against {TargetUserId}: {OldRole} -> {NewRole}")]
    public static partial void AdminUserRoleChanged(ILogger logger, string actorUserId, string targetUserId, string oldRole, string newRole);
}
