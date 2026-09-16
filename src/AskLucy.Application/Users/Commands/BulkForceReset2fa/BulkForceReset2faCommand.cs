using AskLucy.Application.Common;
using MediatR;

namespace AskLucy.Application.Users.Commands.BulkForceReset2fa;

public sealed record BulkForceReset2faCommand(BulkTarget Target, string? Search) : IRequest<BulkActionOutcome>;
