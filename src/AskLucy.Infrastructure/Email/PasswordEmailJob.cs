using System.Net;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Options;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Email;

/// <summary>
/// Renders and sends the two password-flow emails from a Hangfire worker
/// (specs/058-password-recovery US1/US3). Bodies are built in-memory per send, following
/// <c>RegisterCommandHandler</c>'s pattern rather than the legacy shared-template-file approach.
/// </summary>
[AutomaticRetry(Attempts = 3)]
public sealed partial class PasswordEmailJob(
    IEmailTemplateRenderer templateRenderer,
    IEmailSender emailSender,
    IPasswordTokenProtector tokenProtector,
    IOptions<AppOptions> appOptions,
    ILogger<PasswordEmailJob> logger) : IPasswordEmailJob
{
    public async Task SendResetLinkAsync(
        string userId, string email, string protectedToken, CancellationToken cancellationToken = default)
    {
        var plaintextToken = tokenProtector.Unprotect(protectedToken);

        if (plaintextToken is null)
        {
            // Fail loudly rather than mail a link that cannot work. The throw lets Hangfire retry
            // and, once retries are exhausted, leaves a failed job an operator can see — a caught
            // and swallowed failure here would leave the user waiting for an email forever.
            LogUnprotectFailed(logger, userId);
            throw new InvalidOperationException(
                $"Could not unprotect the reset token for user '{userId}'; refusing to send an unusable reset link.");
        }

        // The link carries the user id, not the address (contracts/password-api.md): an opaque id
        // in a URL that lands in inboxes, browser history and referrer headers leaks nothing.
        var resetLink =
            $"{appOptions.Value.FrontendBaseUrl}/reset-password?userId={Uri.EscapeDataString(userId)}&token={Uri.EscapeDataString(plaintextToken)}";

        const string subject = "Reset your Ask Lucy password";
        var content = new AccountEmailContent(
            Subject: subject,
            PreheaderText: "Use this link to choose a new Ask Lucy password.",
            Heading: "Reset your password",
            BodyParagraphs:
            [
                $"Hi {WebUtility.HtmlEncode(email)},",
                "We received a request to reset your Ask Lucy password. Use the button below to choose a new one."
            ],
            SafetyNote: "This link expires in one hour and can be used once. If you did not ask for it, you can ignore this email — your password has not changed.",
            PrimaryAction: new EmailAction("Reset my password", resetLink));

        var (htmlBody, textBody) = templateRenderer.Render(content);
        await emailSender.SendAsync(email, subject, htmlBody, textBody, cancellationToken);

        LogResetLinkSent(logger, userId);
    }

    public async Task SendPasswordChangedNoticeAsync(
        string email, DateTime changedAtUtc, CancellationToken cancellationToken = default)
    {
        const string subject = "Your Ask Lucy password was changed";
        var content = new AccountEmailContent(
            Subject: subject,
            PreheaderText: "Your Ask Lucy password was just changed.",
            Heading: "Your password was changed",
            BodyParagraphs:
            [
                $"Hi {WebUtility.HtmlEncode(email)},",
                $"Your Ask Lucy password was changed on {changedAtUtc:yyyy-MM-dd HH:mm} UTC."
            ],
            SafetyNote: "If this was you, nothing further is needed. If it was not, reset your password immediately and review your active sessions.");

        var (htmlBody, textBody) = templateRenderer.Render(content);
        await emailSender.SendAsync(email, subject, htmlBody, textBody, cancellationToken);

        LogChangeNoticeSent(logger);
    }

    [LoggerMessage(EventId = 5811, Level = LogLevel.Information, Message = "Password reset link emailed. UserId={UserId}")]
    private static partial void LogResetLinkSent(ILogger logger, string userId);

    [LoggerMessage(EventId = 5812, Level = LogLevel.Error, Message = "Refusing to send a password reset link: the queued token could not be unprotected. UserId={UserId}")]
    private static partial void LogUnprotectFailed(ILogger logger, string userId);

    [LoggerMessage(EventId = 5813, Level = LogLevel.Information, Message = "Password-changed notification emailed.")]
    private static partial void LogChangeNoticeSent(ILogger logger);
}
