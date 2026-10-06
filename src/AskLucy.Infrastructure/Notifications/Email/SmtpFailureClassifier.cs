using System.IO;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace AskLucy.Infrastructure.Notifications.Email;

/// <summary>Whether retrying a failed send can help (research R6).</summary>
public enum SmtpFailureClass
{
    /// <summary>A 4xx reply, a timeout, a dropped connection: try again on the schedule.</summary>
    Transient,

    /// <summary>A 5xx reply or an address the message can't be built for: retrying can't help.</summary>
    Permanent,
}

/// <summary>A classified send failure, safe to store, log and show to an administrator.</summary>
/// <param name="Class">Retry or not.</param>
/// <param name="SafeReason">Fixed wording chosen here, never the exception text, so it can't carry a credential, an address or a server banner.</param>
/// <param name="ProviderResponse">The numeric SMTP reply and enhanced status code only, e.g. <c>SMTP 550 5.1.1</c>.</param>
/// <param name="RequiresAttention">An authentication failure: transient for this delivery, but the channel needs an operator (raises the SMTP health alert).</param>
public sealed record SmtpFailure(SmtpFailureClass Class, string SafeReason, string? ProviderResponse, bool RequiresAttention = false);

/// <summary>
/// Maps what MailKit throws onto retry or give up. The stored reason and provider response are built
/// from the status code alone: the exception message routinely holds the server banner, the recipient
/// address or the login name, none of which belongs in a delivery row or a log line (FR-056).
/// </summary>
public static partial class SmtpFailureClassifier
{
    public static SmtpFailure Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            // Authentication is matched before the generic protocol errors: a bad login is the operator's
            // to fix, so it raises the alert, but the delivery itself is retried once it is fixed.
            AuthenticationException => new SmtpFailure(
                SmtpFailureClass.Transient, "The mail server rejected the sign-in; the SMTP credentials need attention.", "SMTP authentication failed", RequiresAttention: true),

            SmtpCommandException command => ClassifyCommand(command),

            SslHandshakeException => new SmtpFailure(
                SmtpFailureClass.Transient, "The secure connection to the mail server could not be established.", "TLS handshake failed"),

            SmtpProtocolException => new SmtpFailure(
                SmtpFailureClass.Transient, "The mail server dropped the connection or sent an unexpected reply.", "SMTP protocol error"),

            ServiceNotConnectedException or ServiceNotAuthenticatedException or SocketException or IOException => new SmtpFailure(
                SmtpFailureClass.Transient, "The connection to the mail server failed.", "SMTP connection error"),

            OperationCanceledException or TimeoutException => new SmtpFailure(
                SmtpFailureClass.Transient, "The mail server did not answer in time.", "SMTP timeout"),

            // MailboxAddress.Parse and header validation: the message itself is unsendable.
            FormatException or ParseException or ArgumentException => new SmtpFailure(
                SmtpFailureClass.Permanent, "The recipient address or message could not be accepted by the mail library.", null),

            _ => new SmtpFailure(
                SmtpFailureClass.Transient, "The email could not be sent because of an unexpected error.", "SMTP unexpected error"),
        };
    }

    private static SmtpFailure ClassifyCommand(SmtpCommandException exception)
    {
        var code = (int)exception.StatusCode;
        var response = ProviderResponseOf(code, exception.Message);

        // Credentials and sender policy replies are the operator's problem, whatever their class.
        if (code is 530 or 534 or 535 or 538)
        {
            return new SmtpFailure(
                SmtpFailureClass.Transient, "The mail server rejected the sign-in; the SMTP credentials need attention.", response, RequiresAttention: true);
        }

        return code switch
        {
            >= 500 and < 600 => new SmtpFailure(
                SmtpFailureClass.Permanent, $"The mail server permanently rejected the message ({code}).", response),
            >= 400 and < 500 => new SmtpFailure(
                SmtpFailureClass.Transient, $"The mail server temporarily refused the message ({code}).", response),
            _ => new SmtpFailure(
                SmtpFailureClass.Transient, "The mail server sent an unexpected reply.", response),
        };
    }

    /// <summary>The reply code and, when the server sent one, its enhanced status code: never free text.</summary>
    private static string ProviderResponseOf(int code, string message)
    {
        var enhanced = EnhancedStatusRegex().Match(message);
        return enhanced.Success ? $"SMTP {code} {enhanced.Value}" : $"SMTP {code}";
    }

    [GeneratedRegex(@"\b[245]\.\d{1,3}\.\d{1,3}\b", RegexOptions.CultureInvariant)]
    private static partial Regex EnhancedStatusRegex();
}
