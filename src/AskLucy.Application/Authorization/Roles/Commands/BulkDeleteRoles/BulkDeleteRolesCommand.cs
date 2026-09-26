using AskLucy.Application.Common;
using MediatR;

namespace AskLucy.Application.Authorization.Roles.Commands.BulkDeleteRoles;

public sealed record BulkDeleteRolesResult(BulkActionOutcome Outcome, IReadOnlyDictionary<string, int> ReassignedUserCounts);

public sealed record BulkDeleteRolesCommand(BulkTarget Target, string? Search) : IRequest<BulkDeleteRolesResult>;
