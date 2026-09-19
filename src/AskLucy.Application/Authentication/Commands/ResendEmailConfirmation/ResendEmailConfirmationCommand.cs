using MediatR;

namespace AskLucy.Application.Authentication.Commands.ResendEmailConfirmation;

/// <summary>
/// Re-issues the account-confirmation link for an address whose first one was lost, offered from
/// the sign-in page when a sign-in is refused for an unconfirmed email.
/// </summary>
public sealed record ResendEmailConfirmationCommand(string Email) : IRequest;
