using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AskLucy.Persistence.Repositories;

/// <summary>
/// Opaque keyset-pagination cursor (research.md Topic 6) for <see cref="UserChatRepository.SearchAsync"/>.
/// Encodes the last row's pin-rank (pinned conversations always sort first, FR-008), the
/// active sort column's value, and the row id (tiebreaker) — everything needed to resume a
/// stable keyset scan across inserts/updates between page loads.
/// </summary>
internal static class ConversationCursor
{
    private sealed record Payload(int Rank, string SortValue, Guid Id);

    public static string Encode(int rank, string sortValue, Guid id)
    {
        var json = JsonSerializer.Serialize(new Payload(rank, sortValue, id));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    public static (int Rank, string SortValue, Guid Id)? Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var payload = JsonSerializer.Deserialize<Payload>(json);
            return payload is null ? null : (payload.Rank, payload.SortValue, payload.Id);
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            // An invalid/tampered cursor is treated as "start from the beginning" rather than
            // a 500 — pagination is a UX affordance, not a security boundary.
            return null;
        }
    }

    public static string EncodeDateTime(DateTime value) => value.Ticks.ToString(CultureInfo.InvariantCulture);

    public static DateTime DecodeDateTime(string value) => new(long.Parse(value, CultureInfo.InvariantCulture));
}
