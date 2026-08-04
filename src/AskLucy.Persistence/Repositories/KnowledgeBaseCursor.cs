using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AskLucy.Persistence.Repositories;

/// <summary>
/// Opaque keyset-pagination cursor for <see cref="KnowledgeBaseRepository.SearchAsync"/>.
/// Encodes the last row's pin-rank (pinned knowledge bases always sort first, FR-028), the
/// active sort column's value, and the row id (tiebreaker) — mirrors <c>ConversationCursor</c>.
/// </summary>
internal static class KnowledgeBaseCursor
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

    public static string EncodeLong(long value) => value.ToString(CultureInfo.InvariantCulture);

    public static long DecodeLong(string value) => long.Parse(value, CultureInfo.InvariantCulture);

    public static string EncodeInt(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static int DecodeInt(string value) => int.Parse(value, CultureInfo.InvariantCulture);
}
