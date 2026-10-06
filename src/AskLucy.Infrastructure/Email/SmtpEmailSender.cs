using AskLucy.Application.Abstractions;
using AskLucy.Application.Options;
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
public sealed class SmtpEmailSender(
    IOptions<SmtpOptions> options,
    SmtpConnectionHolder connections) : IEmailSender
{
    private readonly SmtpOptions _options = options.Value;

    /// <summary>
    /// The hub's send (specs/067). One authenticated connection is reused across a worker batch through
    /// <see cref="SmtpConnectionHolder"/>. <c>From</c> and the envelope sender come only from
    /// <see cref="SmtpOptions"/>; the caller supplies the <c>Message-ID</c> (the delivery identity) and,
    /// optionally, a validated <c>Reply-To</c>. A failure throws with the connection dropped, so the next
    /// send starts clean.
    /// </summary>
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(_options.FromName, _options.FromTransactional));
        mime.To.Add(MailboxAddress.Parse(message.To));
        if (!string.IsNullOrWhiteSpace(message.ReplyTo))
        {
            mime.ReplyTo.Add(MailboxAddress.Parse(message.ReplyTo));
        }

        mime.Subject = message.Subject;
        mime.MessageId = message.MessageId.Trim('<', '>');

        var builder = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody };
        var openStreams = new List<Stream>();
        try
        {
            foreach (var attachment in message.Attachments ?? [])
            {
                var stream = await attachment.OpenRead(cancellationToken);
                openStreams.Add(stream);
                await builder.Attachments.AddAsync(attachment.FileName, stream, ContentType.Parse(attachment.ContentType), cancellationToken);
            }

            mime.Body = builder.ToMessageBody();
            await connections.SendAsync(mime, cancellationToken);
        }
        finally
        {
            foreach (var stream in openStreams)
            {
                await stream.DisposeAsync();
            }
        }
    }

    public async Task SendAsync(string toEmail, string subject, string htmlBody, string textBody, CancellationToken cancellationToken = default)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromTransactional));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody, TextBody = textBody }.ToMessageBody();

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
