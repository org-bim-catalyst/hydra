using MediatR;

namespace AskLucy.Application.Users.Commands.ChangeUserRole;

/// <summary>
/// Admin role change (FR-014). <paramref name="NewRole"/> is <c>"Administrator"</c>,
/// <c>"Super User"</c>, or the sentinel <c>"Regular"</c> (meaning "no privileged role").
/// Only a caller in the <c>"Super User"</c> role may grant/revoke <c>"Administrator"</c>/
/// <c>"Super User"</c> itself (Clarifications 2026-07-28); rejects the last-remaining-Super-User
/// case (FR-023).
/// </summary>
public sealed record ChangeUserRoleCommand(string UserId, string NewRole) : IRequest;
