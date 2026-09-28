using System.Diagnostics;
using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;

namespace AskLucy.Application.Notifications;

/// <summary>
/// Validates a request and adds it to the caller's unit of work as an outbox event (research R2).
/// Every rejection here is a programming error in the emitter, so it throws rather than returning
/// a result: it must fail the emitter's tests, never be handled at runtime.
/// </summary>
public sealed class NotificationPublisher(
    INotificationOutboxStore outbox,
    ICorrelationIdAccessor correlation,
    TimeProvider timeProvider) : INotificationPublisher
{
    private static readonly JsonSerializerOptions VariablesJson = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> HubFilledVariables =
        NotificationTypeDefinition.StandardVariables.Select(v => v.Name).ToHashSet(StringComparer.Ordinal);

    public void Publish(NotificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!NotificationTypeCatalog.TryGet(request.Type, out var definition) || definition is null)
        {
            throw new ArgumentException($"'{request.Type}' is not a catalogued notification type.", nameof(request));
        }

        if (!definition.IsEmitted)
        {
            throw new InvalidOperationException($"'{request.Type}' is catalogued but has no emitter yet (research R27).");
        }

        ValidateVariables(definition, request.Variables);
        ValidateRecipient(definition, request.Recipient);

        if (definition.RequiresItemAccess && request.RelatedItem is null)
        {
            throw new ArgumentException($"'{request.Type}' links to a related item, so the request must carry one.", nameof(request));
        }

        var outboxEvent = NotificationOutboxEvent.Create(
            definition.Key,
            NotificationRecipientJson.Serialize(request.Recipient),
            JsonSerializer.Serialize(request.Variables, VariablesJson),
            CurrentCorrelationId(),
            timeProvider.GetUtcNow().UtcDateTime,
            request.EventKey,
            request.RelatedItem?.Type,
            request.RelatedItem?.Id,
            request.RelatedItem?.ParentId,
            request.Language);

        outbox.Add(outboxEvent);
    }

    private static void ValidateVariables(NotificationTypeDefinition definition, IReadOnlyDictionary<string, string?> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);

        foreach (var name in variables.Keys)
        {
            if (HubFilledVariables.Contains(name))
            {
                throw new ArgumentException($"'{name}' is a standard variable the hub fills; emitters must not pass it.", nameof(variables));
            }

            if (!definition.VariablesFromTemplate && !definition.Declares(name))
            {
                throw new ArgumentException($"'{definition.Key}' does not declare the variable '{name}'.", nameof(variables));
            }
        }
    }

    private static void ValidateRecipient(NotificationTypeDefinition definition, NotificationRecipient recipient)
    {
        switch (recipient)
        {
            case NotificationRecipient.User u:
                RequireId(u.UserId);
                break;

            case NotificationRecipient.Users u:
                if (u.UserIds is null || u.UserIds.Count is 0 or > NotificationRecipient.MaxUsers)
                {
                    throw new ArgumentException($"A Users recipient carries 1 to {NotificationRecipient.MaxUsers} user ids.", nameof(recipient));
                }

                foreach (var id in u.UserIds)
                {
                    RequireId(id);
                }

                break;

            case NotificationRecipient.Audience a:
                if (definition.Key != NotificationTypeKeys.SystemAnnouncementPublished)
                {
                    throw new ArgumentException("Only system announcements may target an audience.", nameof(recipient));
                }

                if (!a.AllActiveUsers && (a.RoleIds is null || a.RoleIds.Count == 0))
                {
                    throw new ArgumentException("An audience is all active users or at least one role.", nameof(recipient));
                }

                break;

            case NotificationRecipient.AddressForUser a:
                RequireId(a.UserId);
                RequireAddress(a.EmailAddress);
                break;

            case NotificationRecipient.AddressLookup a:
                RequireAddress(a.EmailAddress);
                break;

            case NotificationRecipient.SupportMailbox:
                break;

            case null:
                throw new ArgumentNullException(nameof(recipient));

            default:
                throw new ArgumentException($"Unknown recipient kind '{recipient.GetType().Name}'.", nameof(recipient));
        }
    }

    private static void RequireId(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("A recipient user id is required.", nameof(userId));
        }
    }

    private static void RequireAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address) || !address.Contains('@', StringComparison.Ordinal))
        {
            throw new ArgumentException("A recipient email address is required.", nameof(address));
        }
    }

    /// <summary>The request's or job's id; else the current trace; else a fresh id, so every event is traceable.</summary>
    private string CurrentCorrelationId() =>
        correlation.Current
        ?? (Activity.Current is { } activity ? activity.TraceId.ToHexString() : null)
        ?? Guid.CreateVersion7().ToString();
}
