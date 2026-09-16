using AskLucy.Application.Common;
using MediatR;

namespace AskLucy.Application.Users.Commands.BulkDeleteUsers;

public sealed record BulkDeleteUsersCommand(BulkTarget Target, string? Search) : IRequest<BulkActionOutcome>;
