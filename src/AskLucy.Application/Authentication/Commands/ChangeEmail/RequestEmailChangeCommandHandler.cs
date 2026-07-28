using AskLucy.Application.Abstractions;
using AskLucy.Application.Options;
using MediatR;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.Authentication.Commands.ChangeEmail;

/// <summary>Mirrors RegisterCommandHandler's inline-rendered confirmation email pattern.</summary>
public sealed class RequestEmailChangeCommandHandler(
    IIdentityService identityService,
    IEmailSender emailSender,
    IOptions<AppOptions> appOptions) : IRequestHandler<RequestEmailChangeCommand>
{
    public async Task Handle(RequestEmailChangeCommand request, CancellationToken cancellationToken)
    {
        var token = await identityService.GenerateChangeEmailTokenAsync(request.UserId, request.NewEmail, cancellationToken);

        var confirmationLink =
            $"{appOptions.Value.FrontendBaseUrl}/confirm-email-change" +
            $"?userId={Uri.EscapeDataString(request.UserId)}" +
            $"&newEmail={Uri.EscapeDataString(request.NewEmail)}" +
            $"&token={Uri.EscapeDataString(token)}";

        var body =
            $"""
             <p>You requested to change your Ask Lucy account email to this address.</p>
             <p><a href="{confirmationLink}">Confirm email change</a></p>
             <p>If you didn't request this, you can safely ignore this message.</p>
             """;

        await emailSender.SendAsync(request.NewEmail, "Confirm your new Ask Lucy email", body, cancellationToken);
    }
}
