using System.Text.Json;
using System.Text.Json.Serialization;

namespace AskLucy.Application.Conversations;

/// <summary>
/// The one serializer configuration for <c>SuggestedActionOffer</c>, shared between whoever writes
/// <see cref="Domain.Chats.Message.SuggestedActionsJson"/> (<c>AiController</c>) and whoever reads
/// it back (<see cref="Runtime.SelectedActionResolver"/>).
/// <para>
/// A bare <c>JsonSerializer.Serialize(offer)</c> call — with no options — serializes
/// <c>SuggestedActionKind</c> as a raw integer, unlike every other enum this controller ever puts
/// on the wire, which is deliberately reduced to a string first
/// (<c>confirmedBoundary.Source.ToString()</c> and friends). ASP.NET's own
/// <c>JsonStringEnumConverter</c> registration in Program.cs only reaches controller-formatted
/// responses, not an ad-hoc <c>JsonSerializer</c> call — so persistence and re-reading need their
/// own explicit, shared options rather than assuming that global configuration applies here too.
/// </para>
/// </summary>
public static class SuggestedActionJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
