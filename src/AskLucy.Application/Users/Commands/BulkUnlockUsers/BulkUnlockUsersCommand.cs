using AskLucy.Application.Common;
using MediatR;

namespace AskLucy.Application.Users.Commands.BulkUnlockUsers;

public sealed record BulkUnlockUsersCommand(BulkTarget Target, string? Search) : IRequest<BulkActionOutcome>;
