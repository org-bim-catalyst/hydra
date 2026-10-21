using System.Text.Json;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Domain.Conversations;

namespace AskLucy.Application.Conversations.Runtime;

/// <summary>
/// specs/083-map-snapshot — the hand-composed card offered when a request could have meant
/// "give me a picture of it" or "put it on the map": one row takes the snapshot, one row moves
/// the viewer, and the server-appended decline closes it.
///
/// <para>
/// Built here rather than composed by the offer model because its rows must be exactly these two
/// capabilities with exactly these arguments: a model composing the offer would be guessing at an
/// argument list, and one of the two answers moves the user's map — the wrong guess is a camera
/// the user did not ask to move, and the other answer is a picture they did not ask for. The
/// first row is deliberately the picture, because the phrasing that reaches this card is a
/// question about how the place looks.
/// </para>
///
/// <para>
/// A capability row is grounded at dispatch time by <see cref="SelectedActionResolver"/>, which
/// re-checks the capability is still available and dispatches <b>the row's own</b> arguments —
/// never the client's. Both rows therefore need arguments that satisfy their capability's own
/// input schema, which is why the map-move row carries the place under <c>query</c> (the name
/// <see cref="ResolveLocationCapability"/> requires) and the snapshot row carries it under
/// <c>location</c>.
/// </para>
/// </summary>
public static class MapSnapshotOffer
{
    /// <summary>
    /// The card for a named place. The place is required: the map-move row is
    /// <see cref="ResolveLocationCapability"/>, whose input schema requires a <c>query</c>, and a
    /// row with no arguments would be refused at dispatch.
    /// </summary>
    public static SuggestedActionOffer Build(string placeName)
    {
        var snapshot = new SuggestedAction(
            SuggestedActionKind.Capability,
            MapSnapshotCapability.CapabilityKey,
            Text: null,
            "Take a snapshot",
            $"Capture an aerial picture of {placeName} and show it here.",
            ArgumentsJson: JsonSerializer.Serialize(new { location = placeName }));

        var move = new SuggestedAction(
            SuggestedActionKind.Capability,
            ResolveLocationCapability.CapabilityKey,
            Text: null,
            "Move the map there",
            $"Find {placeName} and put it on the map.",
            ArgumentsJson: JsonSerializer.Serialize(new { query = placeName }));

        return new SuggestedActionOffer(
            $"Do you want a picture of {placeName}, or should I put it on the map?",
            [snapshot, move, SuggestedAction.Decline()]);
    }
}
