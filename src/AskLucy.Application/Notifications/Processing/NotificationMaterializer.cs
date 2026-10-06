using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Domain.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.Notifications.Processing;

/// <summary>Who one notification is for, with the account facts routing needs.</summary>
/// <param name="UserId">Null only for the support mailbox.</param>
/// <param name="Kind">How deliveries address the recipient.</param>
/// <param name="Address">Set only for <see cref="RecipientKind.Address"/>.</param>
/// <param name="DisplayName">Fills <c>recipientDisplayName</c>.</param>
/// <param name="HasVerifiedEmail">Whether email can be routed at all (FR-009c).</param>
/// <param name="Overrides">The recipient's sparse preference overrides.</param>
public sealed record MaterializationTarget(
    string? UserId,
    RecipientKind Kind,
    string? Address,
    string? DisplayName,
    bool HasVerifiedEmail,
    IReadOnlyCollection<PreferenceOverride> Overrides);

/// <summary>
/// Builds one <see cref="Notification"/> and its per-channel deliveries for one recipient: routes,
/// resolves the language, renders in-app, and queues the other channels (research R3, R8).
/// Adds nothing to the unit of work; the caller does.
/// </summary>
public sealed class NotificationMaterializer(
    INotificationChannelRegistry channels,
    IEffectiveLanguageResolver languages,
    INotificationTemplateRenderer renderer,
    INotificationLinkBuilder links,
    IOptions<NotificationsOptions> options,
    TimeProvider timeProvider,
    ILogger<NotificationMaterializer> logger)
{
    private const string InactiveRecipientReason = "The recipient account is no longer active.";

    public async Task<Notification> MaterializeAsync(
        NotificationTypeDefinition definition,
        NotificationOutboxEvent outboxEvent,
        IReadOnlyDictionary<string, string?> variables,
        MaterializationTarget target,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var correlationId = outboxEvent.CorrelationId;
        var priority = definition.DefaultPriority;

        // Pre-generated so a route can point at the notification itself (announcements).
        var notificationId = Guid.CreateVersion7();
        var relatedItem = RelatedItemOf(outboxEvent);
        var route = links.BuildRelative(definition, relatedItem, notificationId);

        var decisions = NotificationRouter.Route(
            definition,
            new RecipientRoutingState(target.HasVerifiedEmail),
            target.Overrides,
            channels.AvailableChannels,
            isCritical: false);

        var language = await languages.ResolveAsync(target.UserId, outboxEvent.ExplicitLanguage, cancellationToken);

        RenderedInApp? rendered = null;
        string? renderError = null;
        if (target.UserId is not null && decisions.Any(d => d is { Channel: NotificationChannel.InApp, Deliver: true }))
        {
            try
            {
                rendered = await renderer.RenderInAppAsync(
                    definition, language, NotificationStandardVariables.Build(variables, target.DisplayName, route, outboxEvent.OccurredAtUtc), cancellationToken);
            }
            catch (NotificationRenderException ex)
            {
                renderError = ex.Message;
                NotificationDispatchLog.InAppRenderFailed(logger, ex, definition.Key, outboxEvent.EventKey, correlationId, notificationId);
            }
        }

        var notification = Notification.Create(
            target.UserId,
            definition,
            priority,
            rendered?.Title ?? string.Empty,
            rendered?.Message ?? string.Empty,
            rendered?.Language ?? language,
            correlationId,
            now,
            showInCenter: rendered is not null,
            rendered?.TemplateVersionId,
            relatedItem?.Type,
            relatedItem?.Id,
            route,
            eventKey: outboxEvent.EventKey,
            sourceEventId: outboxEvent.Id,
            actionLabel: rendered?.ActionLabel,
            id: notificationId);

        foreach (var decision in decisions)
        {
            notification.AddDelivery(DeliveryFor(decision, definition, target, priority, rendered, renderError, correlationId, now));
        }

        return notification;
    }

    /// <summary>An account deleted since the event was raised: recorded, but every channel cancelled and nothing shown (R24).</summary>
    public Notification MaterializeForInactiveRecipient(NotificationTypeDefinition definition, NotificationOutboxEvent outboxEvent, string userId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var relatedItem = RelatedItemOf(outboxEvent);
        var notification = Notification.Create(
            userId,
            definition,
            definition.DefaultPriority,
            string.Empty,
            string.Empty,
            outboxEvent.ExplicitLanguage ?? "en",
            outboxEvent.CorrelationId,
            now,
            showInCenter: false,
            relatedItemType: relatedItem?.Type,
            relatedItemId: relatedItem?.Id,
            eventKey: outboxEvent.EventKey,
            sourceEventId: outboxEvent.Id);

        foreach (var channel in definition.SupportedChannels)
        {
            notification.AddDelivery(NotificationDelivery.CreateCancelled(
                channel, definition.DefaultPriority, RecipientKind.User, InactiveRecipientReason, outboxEvent.CorrelationId, now));
        }

        return notification;
    }

    private NotificationDelivery DeliveryFor(
        ChannelDecision decision,
        NotificationTypeDefinition definition,
        MaterializationTarget target,
        NotificationPriority priority,
        RenderedInApp? rendered,
        string? renderError,
        string correlationId,
        DateTime now)
    {
        if (!decision.Deliver)
        {
            return NotificationDelivery.CreateSkipped(
                decision.Channel, priority, target.Kind, decision.SkipReason ?? DeliverySkipReason.ChannelDisabled, correlationId, now);
        }

        if (decision.Channel == NotificationChannel.InApp)
        {
            // In-app is delivered by materializing it; there is no sender to retry it later.
            return rendered is not null
                ? NotificationDelivery.CreateDelivered(NotificationChannel.InApp, priority, rendered.Language, rendered.TemplateVersionId, correlationId, now)
                : NotificationDelivery.CreateFailed(
                    NotificationChannel.InApp, priority, target.Kind, DeliveryFailureKind.RenderError,
                    renderError ?? "The in-app template could not be rendered.", correlationId, now);
        }

        var retry = options.Value.Retry;
        return NotificationDelivery.CreatePending(
            decision.Channel,
            priority,
            target.Kind,
            target.Kind == RecipientKind.Address ? target.Address : null,
            Math.Max(1, retry.MaxAttempts),
            definition.RequestValidity is { } validity ? now + validity : null,
            correlationId,
            now);
    }

    private static RelatedItem? RelatedItemOf(NotificationOutboxEvent outboxEvent) =>
        outboxEvent is { RelatedItemType: { } type, RelatedItemId: { } id }
            ? new RelatedItem(type, id, outboxEvent.RelatedItemParentId)
            : null;
}
