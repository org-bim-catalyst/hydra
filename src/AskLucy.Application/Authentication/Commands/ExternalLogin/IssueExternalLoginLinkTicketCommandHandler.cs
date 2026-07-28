using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Authentication.Commands.ExternalLogin;

public sealed class IssueExternalLoginLinkTicketCommandHandler(IExternalLoginCodeStore codeStore)
    : IRequestHandler<IssueExternalLoginLinkTicketCommand, string>
{
    private static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(5);

    public Task<string> Handle(IssueExternalLoginLinkTicketCommand request, CancellationToken cancellationToken) =>
        Task.FromResult(codeStore.Issue(request.UserId, TicketLifetime));
}
