using AskLucy.Application.Abstractions;
using Microsoft.Extensions.Options;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace AskLucy.Infrastructure.Email;

/// <summary>
/// Migrated from the legacy <c>EmailSender</c>. Unlike the legacy implementation, the
/// email body is always rendered per-call by the caller (see
/// <c>RegisterCommandHandler</c>) — nothing here reads or mutates a shared template file
/// on disk, closing the race condition/template-corruption bug from spec.md § Gap Analysis.
/// </summary>
public sealed class SendGridEmailSender(IOptions<SendGridOptions> options) : IEmailSender
{
    private readonly SendGridOptions _options = options.Value;

    public async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        var client = new SendGridClient(_options.ApiKey);
        var from = new EmailAddress(_options.FromEmail, _options.FromName);
        var to = new EmailAddress(toEmail);

        var message = MailHelper.CreateSingleEmail(from, to, subject, plainTextContent: null, htmlBody);
        message.SetClickTracking(enable: false, enableText: false);

        var response = await client.SendEmailAsync(message, cancellationToken);
        if ((int)response.StatusCode >= 400)
        {
            var body = await response.Body.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"SendGrid request failed with {(int)response.StatusCode}: {body}");
        }
    }
}
