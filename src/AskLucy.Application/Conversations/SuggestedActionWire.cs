using System.Text.Json;
using System.Text.Json.Serialization;
using AskLucy.Domain.Conversations;

namespace AskLucy.Application.Conversations;

/// <summary>
/// One row of an offer as it exists outside the server — in the <c>__ACTIONS__</c> stream event,
/// in the JSON persisted onto the offering message, and in the selection the client sends back
/// (specs/045 contracts/turn-stream.md §2, contracts/suggested-actions-api.md §1).
/// </summary>
/// <remarks>
/// <c>Key</c> is named <c>capabilityKey</c> out here and the arguments travel as a JSON object
/// rather than a string, so this is genuinely a different shape from
/// <see cref="SuggestedAction"/> — not a casing variant of it.
/// </remarks>
public sealed record SuggestedActionWirePayload(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("capabilityKey")] string? CapabilityKey,
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("label")] string? Label,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("arguments")] JsonElement? Arguments,
    [property: JsonPropertyName("isDecline")] bool IsDecline);

/// <summary>The offer envelope as persisted and as streamed: the question plus its rows.</summary>
public sealed record SuggestedActionOfferWire(
    [property: JsonPropertyName("question")] string? Question,
    [property: JsonPropertyName("actions")] IReadOnlyList<SuggestedActionWirePayload>? Actions);

/// <summary>
/// The single crossing point between <see cref="SuggestedActionOffer"/> and the wire — writing and
/// reading both go through here.
///
/// <para>
/// <b>Why one type owns both directions.</b> The writer and the reader were separate before, and
/// they drifted twice. First the writer serialized the raw domain record (PascalCase property
/// names) while the client read camelCase, which reopened every persisted offer card blank
/// (fixed 2026-09-11 by giving the controller its own camelCase payload builder). That fix left
/// the reader — <see cref="Runtime.SelectedActionResolver"/> — still deserializing the domain
/// shape, so from then on it was reading a shape nobody wrote: <c>capabilityKey</c> never bound
/// to <c>Key</c>, and every selection would have failed to match its own row. Second, the payload
/// builder wrote the kind as <c>Kind.ToString()</c> — <c>"Capability"</c> — while both the
/// contract and the resolver's parser say <c>"capability"</c>, so a dispatched selection was
/// rejected before it got that far. Neither was caught because nothing exercised a real offer
/// through a real round-trip: the resolver's own tests wrote the offer with the domain
/// serializer, and every controller test substituted the resolver.
/// </para>
/// </summary>
public static class SuggestedActionWire
{
    /// <summary>The contract's token for a kind. The only spelling written from here on.</summary>
    public static string ToWire(SuggestedActionKind kind) => kind switch
    {
        SuggestedActionKind.FlowVariant => "flowVariant",
        SuggestedActionKind.Capability => "capability",
        SuggestedActionKind.FollowUp => "followUp",
        SuggestedActionKind.Decline => "decline",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unmapped suggested-action kind."),
    };

    /// <summary>
    /// Reads a kind token back. Deliberately case-insensitive: offers written before this type
    /// existed are persisted with the enum name (<c>"Capability"</c>), and a user reopening one of
    /// those conversations must still be able to click its rows.
    /// </summary>
    public static bool TryParseKind(string? kind, out SuggestedActionKind parsed)
    {
        parsed = default;

        // Enum.TryParse also accepts the underlying number ("1"), which is not a token any
        // contract defines and would silently map a malformed request onto a real kind.
        if (string.IsNullOrWhiteSpace(kind) || !char.IsLetter(kind[0]))
        {
            return false;
        }

        return Enum.TryParse(kind, ignoreCase: true, out parsed) && Enum.IsDefined(parsed);
    }

    /// <summary>One row, in the shape the client reads. Shared by the live event and the persisted copy.</summary>
    public static SuggestedActionWirePayload PayloadFor(SuggestedAction action) => new(
        ToWire(action.Kind),
        action.Key,
        action.Text,
        action.Label,
        action.Description,
        string.IsNullOrEmpty(action.ArgumentsJson) ? null : JsonSerializer.Deserialize<JsonElement>(action.ArgumentsJson),
        action.IsDecline);

    /// <summary>The whole offer, as persisted onto the offering message.</summary>
    public static string Serialize(SuggestedActionOffer offer) => Serialize(offer.Question, offer.Actions);

    /// <summary>
    /// The same envelope from the parts the streaming endpoint holds, which has a question and a
    /// row list rather than an assembled <see cref="SuggestedActionOffer"/>. Through
    /// <see cref="SuggestedActionOfferWire"/> deliberately, so the envelope's two property names
    /// are spelled once and <see cref="TryDeserialize"/> reads back the type that was written.
    /// </summary>
    public static string Serialize(string? question, IEnumerable<SuggestedAction> actions) =>
        JsonSerializer.Serialize(new SuggestedActionOfferWire(question, [.. actions.Select(PayloadFor)]));

    /// <summary>
    /// Reads a persisted offer back. Returns null rather than throwing for anything unreadable —
    /// malformed JSON, no rows, a row whose kind is not a kind — because every one of those means
    /// the same thing to the caller: this offer can no longer be answered.
    /// </summary>
    public static SuggestedActionOffer? TryDeserialize(string json)
    {
        SuggestedActionOfferWire? wire;
        try
        {
            wire = JsonSerializer.Deserialize<SuggestedActionOfferWire>(json);
        }
        catch (JsonException)
        {
            return null;
        }

        if (wire?.Actions is not { Count: > 0 } rows)
        {
            return null;
        }

        var actions = new List<SuggestedAction>(rows.Count);
        foreach (var row in rows)
        {
            if (!TryParseKind(row.Kind, out var kind))
            {
                return null;
            }

            actions.Add(new SuggestedAction(
                kind,
                row.CapabilityKey,
                row.Text,
                row.Label ?? string.Empty,
                row.Description ?? string.Empty,
                row.Arguments is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } arguments
                    ? arguments.GetRawText()
                    : null));
        }

        return new SuggestedActionOffer(wire.Question ?? string.Empty, actions);
    }
}
