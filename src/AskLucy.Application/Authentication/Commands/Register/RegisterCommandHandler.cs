using System.Net;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Options;
using MediatR;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.Authentication.Commands.Register;

/// <summary>
/// Renders the confirmation email in-memory, per request — unlike the legacy
/// implementation, which mutated a shared template file on disk for every registration
/// (a race condition, and permanent template corruption after the first send). See
/// spec.md § Gap Analysis.
/// </summary>
public sealed class RegisterCommandHandler(
    IIdentityService identityService,
    IEmailTemplateRenderer templateRenderer,
    IEmailSender emailSender,
    IOptions<AppOptions> appOptions) : IRequestHandler<RegisterCommand, AuthResult>
{
    public async Task<AuthResult> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        var result = await identityService.RegisterAsync(
            request.Email, request.Password, request.FirstName, request.LastName, cancellationToken);

        if (result.Status != IdentityResultStatus.Success || result.UserId is null)
        {
            return new AuthResult(AuthOutcome.Failed, Errors: result.Errors);
        }

        var token = await identityService.GenerateEmailConfirmationTokenAsync(result.UserId, cancellationToken);
        var confirmationLink =
            $"{appOptions.Value.FrontendBaseUrl}/confirm-email?userId={Uri.EscapeDataString(result.UserId)}&token={Uri.EscapeDataString(token)}";

        var displayName = string.IsNullOrWhiteSpace(request.FirstName) ? request.Email : request.FirstName;
        const string subject = "Confirm your Ask Lucy account";
        var content = new AccountEmailContent(
            Subject: subject,
            PreheaderText: "Confirm your email to finish setting up your Ask Lucy account.",
            Heading: "Confirm your account",
            BodyParagraphs:
            [
                $"Hi {WebUtility.HtmlEncode(displayName)},",
                "Please confirm your Ask Lucy account by clicking the button below."
            ],
            SafetyNote: "If you didn't create an Ask Lucy account, you can safely ignore this email.",
            PrimaryAction: new EmailAction("Confirm my email", confirmationLink));

        var (htmlBody, textBody) = templateRenderer.Render(content);
        await emailSender.SendAsync(request.Email, subject, htmlBody, textBody, cancellationToken);

        return new AuthResult(AuthOutcome.Success, result.UserId);
    }
}
