namespace AskLucy.Domain.Authorization;

public enum RoleAuditAction
{
    RoleCreated,
    RoleUpdated,
    RoleDeleted,
    RoleAssigned,
    RoleChanged,
    RoleRemoved,
    PermissionRetired,
    AuthorizationDenied
}
