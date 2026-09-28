using System.Text;
using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;

namespace AskLucy.Persistence.Repositories;

/// <summary>
/// Opaque keyset-pagination cursor for <see cref="NotificationRepository.ListAsync"/> — the same
/// (CreatedAtUtc, Id) shape as <c>DocumentCursor</c>, newest first (FR-014, FR-015). Unlike
/// <c>DocumentCursor</c>/<c>ConversationCursor</c>, a malformed cursor here is a client error, not a
/// silent restart: the contract (contracts/notifications-api.md) requires 400, so <see cref="Decode"/>
/// throws rather than falling back to "start from the beginning".
/// </summary>
internal static class NotificationCursor
{
    private sealed record Payload(long CreatedAtUtcTicks, Guid Id);

    public static string Encode(DateTime createdAtUtc, Guid id)
    {
        var json = JsonSerializer.Serialize(new Payload(createdAtUtc.Ticks, id));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    /// <exception cref="ValidationException">The cursor isn't valid base64 JSON in the expected shape.</exception>
    public static (DateTime CreatedAtUtc, Guid Id)? Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var payload = JsonSerializer.Deserialize<Payload>(json);
            if (payload is null)
            {
                throw MalformedCursorException();
            }

            return (new DateTime(payload.CreatedAtUtcTicks, DateTimeKind.Utc), payload.Id);
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            throw MalformedCursorException();
        }
    }

    private static ValidationException MalformedCursorException() =>
        new([new ValidationFailure("cursor", "The cursor is malformed.")]);
}
