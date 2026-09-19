using AskLucy.Application.Abstractions;
using Hangfire;
using MediatR;

namespace AskLucy.Application.Authentication.Commands.RequestPasswordReset;

/// <summary>
/// Hands the whole request to a background worker and returns (specs/058-password-recovery US1).
/// <para>
/// Deliberately does nothing else. Every decision about the address — does it have an account, is
/// that account confirmed, locked out, or throttled — is account-dependent database work, and
/// doing any of it here would make response time reveal what the neutral 202 body is there to hide
/// (FR-003). Handing over an unparsed address is what makes the request path cost the same for
/// every input. See <see cref="IPasswordResetIssuanceJob"/>.
/// </para>
/// </summary>
public sealed class RequestPasswordResetCommandHandler(IBackgroundJobClient backgroundJobClient)
    : IRequestHandler<RequestPasswordResetCommand>
{
    public Task Handle(RequestPasswordResetCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email;
        var requestedFromIp = request.RequestedFromIp;

        backgroundJobClient.Enqueue<IPasswordResetIssuanceJob>(
            j => j.IssueAsync(email, requestedFromIp, CancellationToken.None));

        return Task.CompletedTask;
    }
}
