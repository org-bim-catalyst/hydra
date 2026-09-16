using MediatR;

namespace AskLucy.Application.Authorization.Assignments.Commands.AssignRole;

/// <summary>
/// Replaces a user's single role (FR-012/FR-013/FR-015) — assigns, changes, or removes
/// (<paramref name="RoleId"/> <see langword="null"/>). <paramref name="ExpectedCurrentRoleId"/>
/// is the caller's optimistic-concurrency token: the role the caller last saw the user holding
/// (or <see langword="null"/> for "no role"). The privileged-role rule (FR-016) and the
/// last-Super-User safeguard (FR-017) are enforced here — this is the single implementation
/// <see cref="AskLucy.Application.Users.Commands.ChangeUserRole.ChangeUserRoleCommandHandler"/>
/// also delegates to (research.md Decision 8, plan.md T085).
/// </summary>
public sealed record AssignRoleCommand(string UserId, string? RoleId, string? ExpectedCurrentRoleId) : IRequest;
