using MediatR;

namespace AskLucy.Application.Authorization.Roles.Commands.DuplicateRole;

/// <summary>
/// Super User only: saves any role - built-in included - under a new name as a custom role holding
/// the source's current effective permissions, ready to be edited on its own.
/// </summary>
public sealed record DuplicateRoleCommand(string SourceRoleId, string Name, string? Description) : IRequest<RoleSummaryDto>;
