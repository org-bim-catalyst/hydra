using AskLucy.Application.Abstractions;
using AskLucy.Application.Options;
using MediatR;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.Authentication.Commands.ChangeEmail;

/// <summary>Mirrors RegisterCommandHandler's inline-rendered confirmation email pattern.</summary>
public sealed class RequestEmailChangeCommandHandler(
    IIdentityService identityService,
    IEmailTemplateRenderer templateRenderer,
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

        const string subject = "Confirm your new Ask Lucy email";
        var content = new AccountEmailContent(
            Subject: subject,
            PreheaderText: "Confirm this address to finish changing your Ask Lucy account email.",
            Heading: "Confirm your new email address",
            BodyParagraphs: ["You requested to change your Ask Lucy account email to this address."],
            SafetyNote: "If you didn't request this, you can safely ignore this message.",
            PrimaryAction: new EmailAction("Confirm email change", confirmationLink));

        var (htmlBody, textBody) = templateRenderer.Render(content);
        await emailSender.SendAsync(request.NewEmail, subject, htmlBody, textBody, cancellationToken);
    }
}
