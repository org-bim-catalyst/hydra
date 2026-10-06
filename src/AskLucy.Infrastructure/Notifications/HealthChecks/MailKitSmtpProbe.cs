using AskLucy.Application.Options;
using AskLucy.Infrastructure.Email;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Notifications.HealthChecks;

/// <summary>A throwaway connection of its own, so the probe never interferes with the delivery worker's reused one.</summary>
public sealed class MailKitSmtpProbe(IOptions<SmtpOptions> smtpOptions, IOptionsMonitor<NotificationsOptions> notificationOptions) : ISmtpProbe
{
    public async Task<string> ProbeAsync(CancellationToken cancellationToken)
    {
        var smtp = smtpOptions.Value;
        var secure = smtp.UseSsl ? SecureSocketOptions.SslOnConnect : smtp.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None;

        using var client = new SmtpClient { Timeout = Math.Max(1, notificationOptions.CurrentValue.Email.SendTimeoutSeconds) * 1000 };
        await client.ConnectAsync(smtp.Host, smtp.Port, secure, cancellationToken);
        if (!string.IsNullOrEmpty(smtp.Username))
        {
            await client.AuthenticateAsync(smtp.Username, smtp.Password, cancellationToken);
        }

        await client.DisconnectAsync(quit: true, cancellationToken);
        return secure switch
        {
            SecureSocketOptions.SslOnConnect => "TLS ok",
            SecureSocketOptions.StartTls => "STARTTLS ok",
            _ => "Connected without encryption",
        };
    }
}
