namespace AskLucy.Application.Abstractions;

/// <summary>Renders the shared branded shell around an <see cref="AccountEmailContent"/>.</summary>
public interface IEmailTemplateRenderer
{
    /// <param name="content">The email's variable content.</param>
    /// <param name="language">Goes on the root element's <c>lang</c> (specs/067 FR-049); the default keeps existing output unchanged.</param>
    /// <param name="direction"><c>ltr</c> or <c>rtl</c>, for the root element's <c>dir</c>.</param>
    (string HtmlBody, string TextBody) Render(AccountEmailContent content, string language = "en", string direction = "ltr");
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
