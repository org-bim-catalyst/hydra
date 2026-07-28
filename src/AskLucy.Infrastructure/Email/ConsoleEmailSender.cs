using AskLucy.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace AskLucy.Infrastructure.Email;

/// <summary>
/// Dev-only stand-in for <see cref="SendGridEmailSender"/> — logs the email instead of
/// sending it, so a fresh clone can complete first registration/login (email confirmation)
/// without a real SendGrid key. Registered only for the Development environment (see
/// <see cref="DependencyInjection.AddInfrastructure"/>) — production always uses SendGrid.
/// </summary>
public sealed class ConsoleEmailSender(ILogger<ConsoleEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        ConsoleEmailSenderLog.EmailNotSent(logger, toEmail, subject, htmlBody);
        return Task.CompletedTask;
    }
}

internal static partial class ConsoleEmailSenderLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "[DEV] Email not actually sent. To: {ToEmail}, Subject: {Subject}\n{HtmlBody}")]
    public static partial void EmailNotSent(ILogger logger, string toEmail, string subject, string htmlBody);
}
