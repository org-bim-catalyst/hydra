using AskLucy.Application.Common;
using MediatR;

namespace AskLucy.Application.Authorization.Assignments.Commands.BulkAssignRole;

public sealed record BulkAssignRoleCommand(string RoleId, BulkTarget Target, string? Search) : IRequest<BulkActionOutcome>;
