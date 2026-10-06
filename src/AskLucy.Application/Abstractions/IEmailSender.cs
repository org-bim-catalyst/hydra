namespace AskLucy.Application.Abstractions;

public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string htmlBody, string textBody, CancellationToken cancellationToken = default);

    /// <summary>
    /// specs/067: the notification hub's send. The caller owns the <see cref="EmailMessage.MessageId"/>
    /// (the delivery identity), <c>From</c> and <c>Return-Path</c> come only from server configuration,
    /// and a failure throws so the caller can classify it and decide whether to retry.
    /// </summary>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>One outgoing email, fully rendered (contracts/module-integration.md).</summary>
/// <param name="To">A single recipient address.</param>
/// <param name="Subject">Header-validated: no CR, LF or control characters (FR-051).</param>
/// <param name="HtmlBody">The branded HTML part.</param>
/// <param name="TextBody">The plain-text alternative.</param>
/// <param name="MessageId"><c>&lt;{deliveryId}@{domain}&gt;</c>, so a provider-side duplicate is traceable to one delivery (R5).</param>
/// <param name="ReplyTo">A validated address; <c>From</c> and <c>Return-Path</c> are never caller-supplied.</param>
/// <param name="Attachments">Streamed at send time; none of the types in the first release attach files.</param>
public sealed record EmailMessage(
    string To,
    string Subject,
    string HtmlBody,
    string TextBody,
    string MessageId,
    string? ReplyTo = null,
    IReadOnlyList<EmailAttachment>? Attachments = null);

public sealed record EmailAttachment(string FileName, string ContentType, Func<CancellationToken, Task<Stream>> OpenRead);
