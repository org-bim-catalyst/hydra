using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Authentication.Commands.ChangePassword;

public sealed record ChangePasswordCommand(string UserId, string CurrentPassword, string NewPassword) : IRequest<IdentityOperationResult>;
