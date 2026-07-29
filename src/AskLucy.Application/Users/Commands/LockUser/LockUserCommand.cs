using MediatR;

namespace AskLucy.Application.Users.Commands.LockUser;

/// <summary>Admin lock (FR-012). Rejects self-targeting (FR-022) and the last-remaining-Super-User case (FR-023).</summary>
public sealed record LockUserCommand(string UserId) : IRequest;
