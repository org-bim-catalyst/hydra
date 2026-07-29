using AskLucy.Application.Abstractions;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace AskLucy.Infrastructure.Email;

/// <summary>
/// Replaces SendGridEmailSender (2026-07-28 decision to move off SendGrid onto the
/// hosting provider's own SMTP relay — site4now.net's, not a subdomain of the app's own
/// custom domain, since the latter's TLS certificate doesn't cover a custom mail hostname
/// and every send failed with a certificate-hostname-mismatch handshake error). Every
/// current call site sends a transactional email (registration/email-change confirmation,
/// password reset, 2FA), so this always sends from <see cref="SmtpOptions.FromTransactional"/>.
/// </summary>
public sealed class SmtpEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    private readonly SmtpOptions _options = options.Value;

    public async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromTransactional));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        var secureSocketOptions = _options.UseSsl
            ? SecureSocketOptions.SslOnConnect
            : _options.UseStartTls
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.None;

        using var client = new SmtpClient();
        await client.ConnectAsync(_options.Host, _options.Port, secureSocketOptions, cancellationToken);

        if (!string.IsNullOrEmpty(_options.Username))
        {
            await client.AuthenticateAsync(_options.Username, _options.Password, cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}
