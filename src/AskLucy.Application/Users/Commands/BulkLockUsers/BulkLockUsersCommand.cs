using AskLucy.Application.Common;
using MediatR;

namespace AskLucy.Application.Users.Commands.BulkLockUsers;

public sealed record BulkLockUsersCommand(BulkTarget Target, string? Search) : IRequest<BulkActionOutcome>;
