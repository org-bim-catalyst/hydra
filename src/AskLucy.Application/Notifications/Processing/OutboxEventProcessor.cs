using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Notifications.Processing;

/// <summary>
/// Turns one claimed outbox event into notifications (research R3, R22, R24). Scoped: the
/// dispatch service gives each event its own scope, so one event's failure can't leave tracked
/// changes behind for the next. Materialization and completing the event commit in one save.
/// </summary>
public sealed class OutboxEventProcessor(
    INotificationOutboxStore outbox,
    INotificationRepository notifications,
    INotificationRecipientDirectory directory,
    INotificationPreferenceRepository preferences,
    IEnumerable<INotificationAccessCheck> accessChecks,
    NotificationMaterializer materializer,
    NotificationCreatedPusher pusher,
    INotificationMetrics metrics,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<OutboxEventProcessor> logger)
{
    private static readonly JsonSerializerOptions VariablesJson = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Processes the event if <paramref name="workerId"/> still holds its lease. Throws on any
    /// failure, including losing a de-duplication race; the caller releases the event with backoff.
    /// </summary>
    public async Task ProcessAsync(Guid eventId, string workerId, CancellationToken cancellationToken)
    {
        var outboxEvent = await outbox.GetClaimedAsync(eventId, workerId, cancellationToken);
        if (outboxEvent is null)
        {
            NotificationDispatchLog.LeaseLost(logger, eventId, workerId);
            return;
        }

        if (!NotificationTypeCatalog.TryGet(outboxEvent.Type, out var definition) || definition is null)
        {
            // Only possible if a type was removed while events for it were queued.
            NotificationDispatchLog.UnknownType(logger, outboxEvent.Type, outboxEvent.EventKey, outboxEvent.CorrelationId, outboxEvent.Id);
            outboxEvent.Complete(OutboxEventOutcome.Rejected, Now());
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return;
        }

        var variables = JsonSerializer.Deserialize<Dictionary<string, string?>>(outboxEvent.VariablesJson, VariablesJson) ?? [];
        var recipient = NotificationRecipientJson.Deserialize(outboxEvent.RecipientJson);

        var result = recipient switch
        {
            NotificationRecipient.User u => await ForUsersAsync(definition, outboxEvent, variables, [u.UserId], address: null, cancellationToken),
            NotificationRecipient.Users u => await ForUsersAsync(definition, outboxEvent, variables, [.. u.UserIds.Distinct(StringComparer.Ordinal)], address: null, cancellationToken),
            NotificationRecipient.AddressForUser a => await ForUsersAsync(definition, outboxEvent, variables, [a.UserId], a.EmailAddress, cancellationToken),
            NotificationRecipient.AddressLookup l => await ForAddressLookupAsync(definition, outboxEvent, variables, l.EmailAddress, cancellationToken),
            NotificationRecipient.SupportMailbox => await ForSupportMailboxAsync(definition, outboxEvent, variables, cancellationToken),

            // Audience arrives with announcements (US6).
            _ => throw new NotSupportedException($"The {recipient.GetType().Name} recipient isn't dispatched yet."),
        };

        foreach (var notification in result.Created)
        {
            notifications.Add(notification);
        }

        outboxEvent.Complete(result.Outcome, Now());

        // A concurrent event with the same key can win the unique index between the de-duplication
        // read and this save. Everything tracked was discarded; throwing releases the event, and the
        // retry's de-duplication read then sees the winner.
        if (!await unitOfWork.TrySaveChangesAsync(INotificationRepository.RecipientEventKeyIndexName, cancellationToken))
        {
            throw new InvalidOperationException("Lost a de-duplication race on the notification event key; retrying.");
        }

        NotificationDispatchLog.Dispatched(logger, definition.Key, outboxEvent.EventKey, outboxEvent.CorrelationId, result.Outcome, result.Created.Count);
        foreach (var notification in result.Created)
        {
            metrics.NotificationCreated(notification.Category, notification.Type);
        }

        await pusher.PushAsync(result.Created, cancellationToken);
    }

    private async Task<DispatchResult> ForUsersAsync(
        NotificationTypeDefinition definition,
        NotificationOutboxEvent outboxEvent,
        IReadOnlyDictionary<string, string?> variables,
        IReadOnlyList<string> userIds,
        string? address,
        CancellationToken cancellationToken)
    {
        var pending = userIds;
        var duplicates = 0;
        if (outboxEvent.EventKey is { } eventKey)
        {
            var existing = await notifications.GetRecipientsWithEventKeyAsync(eventKey, userIds, cancellationToken);
            duplicates = existing.Count;
            pending = [.. userIds.Where(id => !existing.Contains(id))];
        }

        if (pending.Count == 0)
        {
            return new DispatchResult([], OutboxEventOutcome.Duplicate);
        }

        var accounts = await directory.GetAsync(pending, cancellationToken);
        var overrides = await preferences.GetOverridesForUsersAsync(pending, cancellationToken);
        var created = new List<Notification>(pending.Count);
        var denied = 0;

        foreach (var userId in pending)
        {
            if (!accounts.TryGetValue(userId, out var account))
            {
                NotificationDispatchLog.UnknownRecipient(logger, definition.Key, outboxEvent.EventKey, outboxEvent.CorrelationId, userId);
                continue;
            }

            if (!account.IsActive)
            {
                created.Add(materializer.MaterializeForInactiveRecipient(definition, outboxEvent, userId));
                continue;
            }

            if (definition.RequiresItemAccess && !await CanAccessAsync(outboxEvent, userId, cancellationToken))
            {
                denied++;
                NotificationDispatchLog.AccessDenied(logger, definition.Key, outboxEvent.EventKey, outboxEvent.CorrelationId, userId, outboxEvent.RelatedItemType);
                continue;
            }

            // An address recipient is the address being confirmed, so it counts as routable (FR-009c).
            var target = address is null
                ? new MaterializationTarget(userId, RecipientKind.User, null, account.DisplayName, account is { EmailConfirmed: true, Email: not null }, OverridesOf(overrides, userId))
                : new MaterializationTarget(userId, RecipientKind.Address, address, account.DisplayName, HasVerifiedEmail: true, OverridesOf(overrides, userId));

            created.Add(await materializer.MaterializeAsync(definition, outboxEvent, variables, target, cancellationToken));
        }

        var outcome = created.Count > 0 ? OutboxEventOutcome.Materialized
            : denied > 0 ? OutboxEventOutcome.Rejected
            : duplicates > 0 ? OutboxEventOutcome.Duplicate
            : OutboxEventOutcome.NoRecipient;
        return new DispatchResult(created, outcome);
    }

    /// <summary>
    /// A request made by address alone (password reset, confirmation resend), resolved here rather than on the
    /// request thread so the request costs the same whether or not the address has an account (FR-009e). An
    /// address with no active account ends as <see cref="OutboxEventOutcome.NoRecipient"/>: nothing is created,
    /// and the log carries only a hash of the address.
    /// </summary>
    private async Task<DispatchResult> ForAddressLookupAsync(
        NotificationTypeDefinition definition,
        NotificationOutboxEvent outboxEvent,
        IReadOnlyDictionary<string, string?> variables,
        string emailAddress,
        CancellationToken cancellationToken)
    {
        var account = await directory.FindByEmailAsync(emailAddress, cancellationToken);
        if (account is not { IsActive: true })
        {
            var addressHash = NotificationAddressHash.Of(emailAddress);
            NotificationDispatchLog.AddressNotFound(logger, definition.Key, outboxEvent.EventKey, outboxEvent.CorrelationId, addressHash);
            return new DispatchResult([], OutboxEventOutcome.NoRecipient);
        }

        // A confirmation goes to the address being confirmed, which is unverified by definition, so it is
        // routed as an explicit address (FR-009c). A reset goes to the account's verified address, and is
        // skipped if there isn't one (an unconfirmed account can't recover by email: it would bypass verification).
        var routable = definition.SensitiveLinkKind == SensitiveLinkKind.EmailConfirmation ? account.Email : null;
        return await ForUsersAsync(definition, outboxEvent, variables, [account.UserId], routable, cancellationToken);
    }

    private async Task<DispatchResult> ForSupportMailboxAsync(
        NotificationTypeDefinition definition,
        NotificationOutboxEvent outboxEvent,
        IReadOnlyDictionary<string, string?> variables,
        CancellationToken cancellationToken)
    {
        if (outboxEvent.EventKey is { } eventKey && await notifications.ExistsForAddressAsync(eventKey, cancellationToken))
        {
            return new DispatchResult([], OutboxEventOutcome.Duplicate);
        }

        // The mailbox address comes from server configuration at send time (FR-009c), never from the event.
        var target = new MaterializationTarget(null, RecipientKind.SupportMailbox, null, null, HasVerifiedEmail: true, []);
        return new DispatchResult([await materializer.MaterializeAsync(definition, outboxEvent, variables, target, cancellationToken)], OutboxEventOutcome.Materialized);
    }

    /// <summary>Fails closed: a type that needs an access check but has none registered is a wiring error, not an allow.</summary>
    private async Task<bool> CanAccessAsync(NotificationOutboxEvent outboxEvent, string userId, CancellationToken cancellationToken)
    {
        if (outboxEvent is not { RelatedItemType: { } itemType, RelatedItemId: { } itemId })
        {
            return false;
        }

        var check = accessChecks.FirstOrDefault(c => string.Equals(c.ItemType, itemType, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"No INotificationAccessCheck is registered for related-item type '{itemType}'.");

        return await check.CanAccessAsync(userId, itemId, cancellationToken);
    }

    private static IReadOnlyCollection<PreferenceOverride> OverridesOf(
        IReadOnlyDictionary<string, IReadOnlyList<PreferenceOverride>> overrides, string userId) =>
        overrides.TryGetValue(userId, out var list) ? list : [];

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

    private sealed record DispatchResult(IReadOnlyList<Notification> Created, OutboxEventOutcome Outcome);
}
