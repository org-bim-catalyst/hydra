using System.Text;
using System.Text.Json;

namespace AskLucy.Persistence.Repositories;

/// <summary>Opaque keyset-pagination cursor for <see cref="AgentRepository"/>/<see cref="AgentExecutionRepository"/> listings — encodes the last row's sort-column value (ticks) and id (tiebreaker), mirroring <c>PromptCursor</c>.</summary>
internal static class AgentCursor
{
    private sealed record Payload(long SortValueTicks, Guid Id);

    public static string Encode(DateTime sortValue, Guid id)
    {
        var json = JsonSerializer.Serialize(new Payload(sortValue.Ticks, id));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    public static (DateTime SortValue, Guid Id)? Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var payload = JsonSerializer.Deserialize<Payload>(json);
            return payload is null ? null : (new DateTime(payload.SortValueTicks, DateTimeKind.Utc), payload.Id);
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            // An invalid/tampered cursor is treated as "start from the beginning" rather than a
            // 500 — pagination is a UX affordance, not a security boundary.
            return null;
        }
    }
}
