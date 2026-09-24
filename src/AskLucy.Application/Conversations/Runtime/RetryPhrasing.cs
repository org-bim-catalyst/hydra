using System.Text.RegularExpressions;

namespace AskLucy.Application.Conversations.Runtime;

/// <summary>
/// Recognises a typed retry (specs/068 FR-012, SC-003, SC-010).
///
/// <para>
/// <b>Why this is deterministic rather than a model judgement.</b> SC-003 and SC-010 both fix a
/// routing accuracy of at least 95%, and a target nobody can measure is not a target. A closed set
/// of phrasings matched here is measurable in a unit test, costs nothing, and cannot regress
/// silently when a provider changes its model behind the same name. The router still sees the
/// recent-outcome summary and can route a retry the model's way; this is the floor under it, not a
/// replacement for it.
/// </para>
///
/// <para>
/// <b>Deliberately narrow.</b> Only messages that are *nothing but* a retry request match. "Try
/// again with the other entrance" carries new information and belongs to the router, because
/// replaying the recorded arguments would quietly discard what the user just said.
/// </para>
/// </summary>
public static partial class RetryPhrasing
{
    /// <summary>True when the message asks for the previous action to be attempted again, and nothing else.</summary>
    public static bool IsRetryRequest(string? message) =>
        !string.IsNullOrWhiteSpace(message) && Retry().IsMatch(Normalize(message));

    /// <summary>Lower case, no punctuation, single-spaced — so "Try again!" and "try again" are one phrasing.</summary>
    private static string Normalize(string message) =>
        Whitespace().Replace(Punctuation().Replace(message.ToLowerInvariant(), " "), " ").Trim();

    // The trailing intensifier is optional and separate so "try again one more time" reads the
    // same as "try again" — users stack these, and rejecting the stacked form would push a plain
    // retry onto the router for no reason.
    // Anchored at both ends: anything the user added around the request is new information, and
    // the router — which can read it — gets that turn instead. "retry" says it by itself; every
    // other verb needs the "again" to distinguish a retry from a fresh instruction ("do it").
    [GeneratedRegex(@"^(?:please )?(?:can you |could you |would you |will you )?(?:just )?(?:retry(?: (?:it|that|this|the same))?(?: (?:again|once more|one more time|another time))?|(?:try|attempt|run|do)(?: (?:it|that|this|the same))?(?: (?:again|once more|one more time|another time)))(?: (?:once more|one more time|another time))?(?: please)?$")]
    private static partial Regex Retry();

    [GeneratedRegex(@"[^\p{L}\p{N}\s]")]
    private static partial Regex Punctuation();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
