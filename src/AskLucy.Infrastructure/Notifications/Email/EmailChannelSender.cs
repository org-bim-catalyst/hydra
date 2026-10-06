using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Domain.Notifications;
using AskLucy.Infrastructure.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Notifications.Email;

/// <summary>
/// Sends one email delivery (specs/067 US3): takes a send-limiter token, renders the published template,
/// builds the message with its deterministic <c>Message-ID</c>, sends it and turns whatever happens into a
/// classified <see cref="ChannelSendResult"/>. A delivery problem is returned, never thrown; only a host
/// shutdown propagates, so the worker can leave the delivery for the lease sweeper (R5).
/// </summary>
public sealed class EmailChannelSender(
    EmailSendRateLimiter limiter,
    INotificationTemplateRenderer renderer,
    IEmailSender emailSender,
    IOptions<SmtpOptions> smtpOptions,
    IOptionsMonitor<NotificationsOptions> options,
    ILogger<EmailChannelSender> logger) : INotificationChannelSender
{
    /// <summary>Used only when the configured From address has no domain, which would be a misconfiguration.</summary>
    private const string FallbackMessageIdDomain = "asklucy.invalid";

    public NotificationChannel Channel => NotificationChannel.Email;

    public async Task<ChannelSendResult> SendAsync(DeliveryContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        // The limiter comes first: a deferral costs nothing, and rendering a delivery that can't be
        // sent yet would be wasted work.
        if (!limiter.TryAcquire(context.IsMandatory, out var retryAfter))
        {
            EmailChannelLog.Deferred(logger, context.DeliveryId, context.CorrelationId, retryAfter);
            return ChannelSendResult.Deferred(retryAfter);
        }

        RenderedEmail rendered;
        try
        {
            rendered = await renderer.RenderEmailAsync(context.Definition, context.Language, context.Variables, cancellationToken);
        }
        catch (NotificationRenderException ex)
        {
            EmailChannelLog.RenderFailed(logger, ex, context.Definition.Key, context.DeliveryId, context.CorrelationId);
            return ChannelSendResult.Permanent(DeliveryFailureKind.RenderError, "The email template could not be rendered.");
        }

        var message = new EmailMessage(
            context.RecipientAddress,
            rendered.Subject,
            rendered.HtmlBody,
            rendered.TextBody,
            MessageIdFor(context.DeliveryId, smtpOptions.Value.FromTransactional));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.CurrentValue.Email.SendTimeoutSeconds)));

        try
        {
            // From here the message may leave the process; a shutdown after this point leaves the outcome unknown.
            context.Progress?.MarkTransmissionStarted();
            await emailSender.SendAsync(message, timeout.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The host is stopping mid-send. The outcome is unknown, so it is not classified here.
            throw;
        }
        catch (Exception ex)
        {
            var failure = SmtpFailureClassifier.Classify(ex);

            // The exception goes to the log (it may name the server); the delivery row gets only the
            // classified, fixed-wording reason. The recipient address is never logged.
            EmailChannelLog.SendFailed(logger, ex, context.Definition.Key, context.DeliveryId, context.CorrelationId, failure.Class);
            return failure.Class == SmtpFailureClass.Permanent
                ? ChannelSendResult.Permanent(DeliveryFailureKind.Permanent, failure.SafeReason, failure.ProviderResponse)
                : ChannelSendResult.Transient(failure.SafeReason, failure.ProviderResponse, failure.RequiresAttention);
        }

        EmailChannelLog.Sent(logger, context.Definition.Key, context.DeliveryId, context.CorrelationId);
        return ChannelSendResult.Sent("SMTP accepted", rendered.TemplateVersionId, rendered.Language);
    }

    /// <summary><c>&lt;{deliveryId}@{domain}&gt;</c>: the same delivery always yields the same id, so a receiver can collapse a duplicate (R5).</summary>
    internal static string MessageIdFor(Guid deliveryId, string fromAddress)
    {
        var at = fromAddress.LastIndexOf('@');
        var domain = at >= 0 && at < fromAddress.Length - 1 ? fromAddress[(at + 1)..].Trim() : FallbackMessageIdDomain;
        return $"<{deliveryId:D}@{domain}>";
    }
}

internal static partial class EmailChannelLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Email delivery {DeliveryId} (CorrelationId {CorrelationId}) deferred: the send limiter has no capacity for {RetryAfter}.")]
    public static partial void Deferred(ILogger logger, Guid deliveryId, string correlationId, TimeSpan retryAfter);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "The email template for {Type} could not be rendered (DeliveryId {DeliveryId}, CorrelationId {CorrelationId}).")]
    public static partial void RenderFailed(ILogger logger, Exception exception, string type, Guid deliveryId, string correlationId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Sending the {Type} email failed (DeliveryId {DeliveryId}, CorrelationId {CorrelationId}); classified {FailureClass}.")]
    public static partial void SendFailed(ILogger logger, Exception exception, string type, Guid deliveryId, string correlationId, SmtpFailureClass failureClass);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Sent the {Type} email (DeliveryId {DeliveryId}, CorrelationId {CorrelationId}).")]
    public static partial void Sent(ILogger logger, string type, Guid deliveryId, string correlationId);
}
