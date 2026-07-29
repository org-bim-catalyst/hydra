using MediatR;

namespace AskLucy.Application.Users.Commands.ForceReset2fa;

/// <summary>Admin force-2FA-reset (FR-015). Rejects self-targeting (FR-022).</summary>
public sealed record ForceReset2faCommand(string UserId) : IRequest;
