using MediatR;

namespace AskLucy.Application.Users.Commands.AdminSendPasswordReset;

/// <summary>Admin-triggered password reset link, mirroring the self-service forgot-password flow. Rejects self-targeting.</summary>
public sealed record AdminSendPasswordResetCommand(string UserId) : IRequest;
