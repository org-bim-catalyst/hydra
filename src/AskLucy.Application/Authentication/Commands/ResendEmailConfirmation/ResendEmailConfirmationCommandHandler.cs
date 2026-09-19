using AskLucy.Application.Abstractions;
using Hangfire;
using MediatR;

namespace AskLucy.Application.Authentication.Commands.ResendEmailConfirmation;

/// <summary>
/// Hands the address straight to a background worker, for the reason spelled out in
/// <c>RequestPasswordResetCommandHandler</c>: every lookup that could tell "this address has an
/// unconfirmed account" apart from "this address has none" is database work whose duration would
/// undo the neutral 202.
/// </summary>
public sealed class ResendEmailConfirmationCommandHandler(IBackgroundJobClient backgroundJobClient)
    : IRequestHandler<ResendEmailConfirmationCommand>
{
    public Task Handle(ResendEmailConfirmationCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email;
        backgroundJobClient.Enqueue<IAccountEmailJob>(j => j.ResendConfirmationAsync(email, CancellationToken.None));
        return Task.CompletedTask;
    }
}
