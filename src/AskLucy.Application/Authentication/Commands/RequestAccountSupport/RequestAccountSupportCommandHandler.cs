using AskLucy.Application.Abstractions;
using Hangfire;
using MediatR;

namespace AskLucy.Application.Authentication.Commands.RequestAccountSupport;

public sealed class RequestAccountSupportCommandHandler(IBackgroundJobClient backgroundJobClient)
    : IRequestHandler<RequestAccountSupportCommand>
{
    public Task Handle(RequestAccountSupportCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email;
        var message = request.Message;
        var requestedFromIp = request.RequestedFromIp;

        backgroundJobClient.Enqueue<IAccountEmailJob>(
            j => j.SendAccountSupportRequestAsync(email, message, requestedFromIp, CancellationToken.None));

        return Task.CompletedTask;
    }
}
