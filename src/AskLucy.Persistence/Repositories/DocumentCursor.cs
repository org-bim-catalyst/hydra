using System.Text;
using System.Text.Json;

namespace AskLucy.Persistence.Repositories;

/// <summary>Opaque keyset-pagination cursor for <see cref="DocumentRepository.SearchAsync"/> — mirrors <c>KnowledgeBaseCursor</c>, without the pin-rank concept (documents aren't pinned in this spec).</summary>
internal static class DocumentCursor
{
    private sealed record Payload(long CreatedAtUtcTicks, Guid Id);

    public static string Encode(DateTime createdAtUtc, Guid id)
    {
        var json = JsonSerializer.Serialize(new Payload(createdAtUtc.Ticks, id));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

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
            return payload is null ? null : (new DateTime(payload.CreatedAtUtcTicks, DateTimeKind.Utc), payload.Id);
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            // An invalid/tampered cursor starts from the beginning rather than a 500 —
            // pagination is a UX affordance, not a security boundary (mirrors KnowledgeBaseCursor).
            return null;
        }
    }
}
