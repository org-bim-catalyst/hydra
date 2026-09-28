using System.Text.Json;
using AskLucy.Application.Notifications.Abstractions;

namespace AskLucy.Application.Notifications;

/// <summary>
/// The outbox's <c>RecipientJson</c> format. A flat, explicitly discriminated shape rather than
/// polymorphic serialization, so the stored payload doesn't depend on CLR type names and stays
/// readable in the database.
/// </summary>
public static class NotificationRecipientJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(NotificationRecipient recipient)
    {
        var payload = recipient switch
        {
            NotificationRecipient.User u => new Payload(nameof(NotificationRecipient.User)) { UserId = u.UserId },
            NotificationRecipient.Users u => new Payload(nameof(NotificationRecipient.Users)) { UserIds = [.. u.UserIds] },
            NotificationRecipient.Audience a => new Payload(nameof(NotificationRecipient.Audience)) { AllActiveUsers = a.AllActiveUsers, RoleIds = [.. a.RoleIds] },
            NotificationRecipient.AddressForUser a => new Payload(nameof(NotificationRecipient.AddressForUser)) { UserId = a.UserId, EmailAddress = a.EmailAddress },
            NotificationRecipient.AddressLookup a => new Payload(nameof(NotificationRecipient.AddressLookup)) { EmailAddress = a.EmailAddress },
            NotificationRecipient.SupportMailbox => new Payload(nameof(NotificationRecipient.SupportMailbox)),
            _ => throw new ArgumentOutOfRangeException(nameof(recipient), recipient.GetType().Name, "Unknown notification recipient kind."),
        };

        return JsonSerializer.Serialize(payload, Options);
    }

    /// <summary>Throws <see cref="JsonException"/> for a payload this version can't read.</summary>
    public static NotificationRecipient Deserialize(string json)
    {
        var p = JsonSerializer.Deserialize<Payload>(json, Options)
            ?? throw new JsonException("The notification recipient payload is empty.");

        return p.Kind switch
        {
            nameof(NotificationRecipient.User) => new NotificationRecipient.User(Required(p.UserId)),
            nameof(NotificationRecipient.Users) => new NotificationRecipient.Users(p.UserIds ?? []),
            nameof(NotificationRecipient.Audience) => new NotificationRecipient.Audience(p.AllActiveUsers, p.RoleIds ?? []),
            nameof(NotificationRecipient.AddressForUser) => new NotificationRecipient.AddressForUser(Required(p.UserId), Required(p.EmailAddress)),
            nameof(NotificationRecipient.AddressLookup) => new NotificationRecipient.AddressLookup(Required(p.EmailAddress)),
            nameof(NotificationRecipient.SupportMailbox) => new NotificationRecipient.SupportMailbox(),
            _ => throw new JsonException($"Unknown notification recipient kind '{p.Kind}'."),
        };
    }

    private static string Required(string? value) =>
        string.IsNullOrWhiteSpace(value) ? throw new JsonException("The notification recipient payload is missing a required field.") : value;

    private sealed record Payload(string Kind)
    {
        public string? UserId { get; init; }

        public List<string>? UserIds { get; init; }

        public bool AllActiveUsers { get; init; }

        public List<string>? RoleIds { get; init; }

        public string? EmailAddress { get; init; }
    }
}
