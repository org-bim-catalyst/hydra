using System.Net;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Options;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Email;

/// <summary>
/// Sends the two emails a signed-out visitor can trigger from the sign-in page. Bodies are built
/// in-memory per send, following <see cref="PasswordEmailJob"/>.
/// </summary>
[AutomaticRetry(Attempts = 3)]
public sealed partial class AccountEmailJob(
    IEmailSender emailSender,
    IIdentityService identityService,
    IOptions<AppOptions> appOptions,
    IOptions<SmtpOptions> smtpOptions,
    ILogger<AccountEmailJob> logger) : IAccountEmailJob
{
    public async Task ResendConfirmationAsync(string email, CancellationToken cancellationToken = default)
    {
        var userId = await identityService.FindIdByEmailAsync(email, cancellationToken);
        if (userId is null)
        {
            // Not an error: the endpoint answers 202 for every address precisely so that an
            // address without an account is indistinguishable from one with. Logged at
            // Information so the absence of a delivery is still explainable from the log.
            LogResendSkipped(logger, "no account for the address");
            return;
        }

        var eligibility = await identityService.GetPasswordResetEligibilityAsync(userId, cancellationToken);
        if (eligibility is null || eligibility.EmailConfirmed)
        {
            LogResendSkipped(logger, "account already confirmed or no longer exists");
            return;
        }

        var token = await identityService.GenerateEmailConfirmationTokenAsync(userId, cancellationToken);
        var confirmationLink =
            $"{appOptions.Value.FrontendBaseUrl}/confirm-email?userId={Uri.EscapeDataString(userId)}&token={Uri.EscapeDataString(token)}";

        var body =
            $"""
             <p>Hi {WebUtility.HtmlEncode(email)},</p>
             <p>Here is a fresh link to confirm your Ask Lucy account:</p>
             <p><a href="{confirmationLink}">Confirm my email</a></p>
             <p>If you did not ask for this, you can ignore this email.</p>
             """;

        await emailSender.SendAsync(email, "Confirm your Ask Lucy account", body, cancellationToken);

        LogConfirmationResent(logger, userId);
    }

    public async Task SendAccountSupportRequestAsync(
        string fromEmail, string message, string? requestedFromIp, CancellationToken cancellationToken = default)
    {
        var supportMailbox = smtpOptions.Value.FromSupport;

        // The sender's address goes in the body rather than the envelope: relaying an arbitrary
        // anonymous address as the From would fail SPF/DMARC at the receiving end and make the
        // endpoint usable for spoofing.
        var body =
            $"""
             <p>A signed-out user asked for help getting back into their account.</p>
             <p><strong>Account:</strong> {WebUtility.HtmlEncode(fromEmail)}<br />
             <strong>Received:</strong> {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC<br />
             <strong>Origin IP:</strong> {WebUtility.HtmlEncode(requestedFromIp ?? "unknown")}</p>
             <p><strong>Message:</strong></p>
             <blockquote>{WebUtility.HtmlEncode(message).ReplaceLineEndings("<br />")}</blockquote>
             """;

        await emailSender.SendAsync(supportMailbox, "Account access request", body, cancellationToken);

        LogSupportRequestSent(logger);
    }

    [LoggerMessage(EventId = 5831, Level = LogLevel.Information, Message = "Account confirmation link re-sent. UserId={UserId}")]
    private static partial void LogConfirmationResent(ILogger logger, string userId);

    [LoggerMessage(EventId = 5832, Level = LogLevel.Information, Message = "Confirmation resend produced no email: {Reason}")]
    private static partial void LogResendSkipped(ILogger logger, string reason);

    [LoggerMessage(EventId = 5833, Level = LogLevel.Information, Message = "Account support request relayed to the support mailbox.")]
    private static partial void LogSupportRequestSent(ILogger logger);
}
