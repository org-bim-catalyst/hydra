using MediatR;

namespace AskLucy.Application.Users.Commands.UnlockUser;

/// <summary>Admin unlock (FR-013).</summary>
public sealed record UnlockUserCommand(string UserId) : IRequest;
