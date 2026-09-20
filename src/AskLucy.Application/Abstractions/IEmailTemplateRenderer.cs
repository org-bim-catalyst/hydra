namespace AskLucy.Application.Abstractions;

/// <summary>Renders the shared branded shell around an <see cref="AccountEmailContent"/>.</summary>
public interface IEmailTemplateRenderer
{
    (string HtmlBody, string TextBody) Render(AccountEmailContent content);
}

/// <summary>
/// Describes one account email's variable content; the renderer owns the shared visual shell.
/// Caller-supplied values (display name, email address, IP address) must already be
/// HTML-encoded before being placed into <see cref="BodyParagraphs"/>.
/// </summary>
public sealed record AccountEmailContent(
    string Subject,
    string PreheaderText,
    string Heading,
    IReadOnlyList<string> BodyParagraphs,
    string SafetyNote,
    string? Greeting = null,
    EmailAction? PrimaryAction = null,
    string? FooterNote = null);

/// <summary>The single emphasized call-to-action in an account email.</summary>
public sealed record EmailAction(string Label, string Url);
