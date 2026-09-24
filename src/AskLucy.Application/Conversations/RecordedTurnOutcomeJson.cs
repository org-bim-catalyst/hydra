using System.Text.Json;
using System.Text.Json.Serialization;

namespace AskLucy.Application.Conversations;

/// <summary>
/// specs/068 — the one serializer configuration for <see cref="Runtime.RecordedTurnOutcome"/>,
/// shared between whoever writes <see cref="Domain.Chats.Message.TurnOutcomeJson"/> (the chat
/// controller) and whoever reads it back (the transcript projection, the claim gate, retry
/// resolution).
/// <para>
/// Same reasoning as <see cref="SuggestedActionWire"/>: ASP.NET's global
/// <c>JsonStringEnumConverter</c> registration reaches controller-formatted responses only, never
/// an ad-hoc <c>JsonSerializer</c> call, so a bare <c>Serialize</c> here would persist
/// <see cref="Runtime.TurnVerdict"/> as a raw integer. Web defaults additionally make the read
/// side case-insensitive, so a document written by an older build still parses.
/// </para>
/// <para>
/// One options instance for both the persisted document and the wire shape means a reloaded
/// outcome parses with exactly the code that parsed the streamed one (SC-001c).
/// </para>
/// </summary>
public static class RecordedTurnOutcomeJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
