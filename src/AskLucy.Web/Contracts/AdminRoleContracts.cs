namespace AskLucy.Web.Contracts;

public sealed record CreateRoleRequest(string Name, string? Description, IReadOnlyList<string> PermissionKeys);

public sealed record UpdateRoleRequest(string Name, string? Description, IReadOnlyList<string> PermissionKeys, string ConcurrencyStamp);

public sealed record UpdateDefaultRoleRequest(string? Description, IReadOnlyList<string> PermissionKeys, string ConcurrencyStamp);

public sealed record DuplicateRoleRequest(string Name, string? Description);

public sealed record AssignRoleRequest(string? RoleId, string? ExpectedCurrentRoleId);

public sealed record SetAdministratorContentAccessRequest(bool Granted);
