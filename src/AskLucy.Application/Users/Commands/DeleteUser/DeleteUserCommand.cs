using MediatR;

namespace AskLucy.Application.Users.Commands.DeleteUser;

/// <summary>Admin soft-delete (FR-016). Rejects self-targeting (FR-022) and the last-remaining-Super-User case (FR-023).</summary>
public sealed record DeleteUserCommand(string UserId) : IRequest;
