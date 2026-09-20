using MediatR;

namespace AskLucy.Application.Users.Commands.AdminResendConfirmation;

/// <summary>Admin-triggered confirmation-link resend for a not-yet-confirmed account. Rejects self-targeting.</summary>
public sealed record AdminResendConfirmationCommand(string UserId) : IRequest;
