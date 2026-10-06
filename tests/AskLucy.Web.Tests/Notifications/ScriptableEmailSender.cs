using System.Collections.Concurrent;
using AskLucy.Application.Abstractions;
using MailKit.Net.Smtp;

namespace AskLucy.Web.Tests.Notifications;

/// <summary>What a scripted send does on one attempt.</summary>
public enum EmailFault
{
    /// <summary>The server accepts the message.</summary>
    None,

    /// <summary>A 4xx reply: transient.</summary>
    Temporary450,

    /// <summary>The connection drops mid-conversation: transient.</summary>
    DroppedConnection,

    /// <summary>A 550 reply for the recipient: permanent.</summary>
    RecipientRejected550,

    /// <summary>The worker is stopped mid-send, after the message may already have left (R5).</summary>
    CrashMidSend,
}

/// <summary>
/// A fake SMTP server for the whole test process (specs/067 T108, quickstart §4). Every
/// <see cref="CustomWebApplicationFactory"/> host shares <see cref="Shared"/>: all hosts in a test run
/// share one database, and each runs its own delivery worker, so any of them may pick up a delivery a test
/// queued. A per-host fake would let another host's real sender receive it, and the test could not tell.
/// The default policy accepts everything, so tests that don't script it are unaffected.
/// </summary>
public sealed class ScriptableEmailSender : IEmailSender
{
    public static ScriptableEmailSender Shared { get; } = new();

    private readonly ConcurrentDictionary<string, int> _attempts = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _accepted = new();
    private readonly ConcurrentQueue<(string To, string Subject, string MessageId)> _sent = new();
    private readonly ConcurrentQueue<EmailMessage> _messages = new();

    /// <summary>Decides the fault for a message on its n-th attempt (1-based). Replace it for a test; call <see cref="Reset"/> afterwards.</summary>
    public Func<EmailMessage, int, EmailFault> Policy { get; set; } = static (_, _) => EmailFault.None;

    /// <summary>
    /// What "the worker process is killed" does for a <see cref="EmailFault.CrashMidSend"/>: it must stop the
    /// worker(s) that might be calling, which cancels the token passed to <see cref="SendAsync(EmailMessage, CancellationToken)"/>.
    /// </summary>
    public Action? OnCrash { get; set; }

    /// <summary>Message-IDs the server accepted, one entry per acceptance: a duplicate here is a duplicate email.</summary>
    public IReadOnlyCollection<string> Accepted => _accepted;

    public IReadOnlyCollection<(string To, string Subject, string MessageId)> Sent => _sent;

    /// <summary>Every message the server accepted, in full, for tests that read what a recipient would see.</summary>
    public IReadOnlyCollection<EmailMessage> AcceptedMessages => _messages;

    public int AttemptsFor(string messageId) => _attempts.GetValueOrDefault(messageId);

    /// <summary>
    /// Puts the fault script back to "accept everything". It deliberately keeps what was already recorded: the
    /// server is shared by every host in the process, so another test may be waiting for a message it has
    /// already received, and Message-IDs are unique, so old entries never get in a test's way.
    /// </summary>
    public void Reset()
    {
        Policy = static (_, _) => EmailFault.None;
        OnCrash = null;
    }

    public Task SendAsync(string toEmail, string subject, string htmlBody, string textBody, CancellationToken cancellationToken = default)
    {
        _sent.Enqueue((toEmail, subject, string.Empty));
        _messages.Enqueue(new EmailMessage(toEmail, subject, htmlBody, textBody, string.Empty));
        return Task.CompletedTask;
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var attempt = _attempts.AddOrUpdate(message.MessageId, 1, (_, n) => n + 1);
        var fault = Policy(message, attempt);
        switch (fault)
        {
            case EmailFault.Temporary450:
                throw new SmtpCommandException(SmtpErrorCode.MessageNotAccepted, SmtpStatusCode.MailboxBusy, "450 4.2.0 mailbox busy, try again");
            case EmailFault.DroppedConnection:
                throw new IOException("Unable to read data from the transport connection.");
            case EmailFault.RecipientRejected550:
                throw new SmtpCommandException(SmtpErrorCode.RecipientNotAccepted, SmtpStatusCode.MailboxUnavailable, "550 5.1.1 user unknown");
            case EmailFault.CrashMidSend:
                // The message may have reached the recipient before the process died: record it as accepted
                // once, the conservative reading, so a later resend would show up as a duplicate.
                _accepted.Enqueue(message.MessageId);
                _sent.Enqueue((message.To, message.Subject, message.MessageId));
                (OnCrash ?? throw new InvalidOperationException("A crash fault needs OnCrash to stop the worker.")).Invoke();
                cancellationToken.ThrowIfCancellationRequested();
                throw new InvalidOperationException("OnCrash did not stop the worker that made this call.");
            default:
                _accepted.Enqueue(message.MessageId);
                _sent.Enqueue((message.To, message.Subject, message.MessageId));
                _messages.Enqueue(message);
                return Task.CompletedTask;
        }
    }
}
