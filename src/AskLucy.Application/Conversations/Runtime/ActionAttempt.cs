namespace AskLucy.Application.Conversations.Runtime;

/// <summary>
/// One action a turn attempted, and what came of it (specs/068 FR-003, FR-006).
///
/// <para>
/// <see cref="Succeeded"/> is the <b>sole</b> source for any success statement the user reads.
/// Nothing else — not the prose the model produced, not the presence of a stream event — may be
/// read as evidence that an action happened.
/// </para>
///
/// <para>
/// Mirrors <c>AgentToolResult</c>'s <c>Succeeded</c>/<c>FailureReason</c> pair so an attempt can be
/// recorded straight from a capability's own reported result without reinterpretation.
/// </para>
/// </summary>
/// <param name="Kind">Capability kind, matching the suggested-action vocabulary.</param>
/// <param name="Key">Capability key, where the kind is parameterised.</param>
/// <param name="TargetLabel">Human-readable target ("Al Safa Park 2"), for the routing summary.</param>
/// <param name="ArgumentsJson">
/// The <b>server-resolved</b> arguments, never client-supplied input. This is what makes replay
/// safe (research.md D4) and why it is never emitted to the client
/// (contracts/turn-outcome.md).
/// </param>
/// <param name="Succeeded">The action's own reported result.</param>
/// <param name="FailureReason">Required when <paramref name="Succeeded"/> is false (FR-014).</param>
public sealed record ActionAttempt(
    string Kind,
    string? Key,
    string? TargetLabel,
    string ArgumentsJson,
    bool Succeeded,
    string? FailureReason)
{
    /// <summary>An attempt that did what it said.</summary>
    public static ActionAttempt Success(string kind, string? key, string? targetLabel, string argumentsJson) =>
        new(Require(kind), key, targetLabel, argumentsJson ?? "{}", true, null);

    /// <summary>An attempt that did not. A reason is mandatory — FR-014 has to surface one.</summary>
    public static ActionAttempt Failure(string kind, string? key, string? targetLabel, string argumentsJson, string failureReason)
    {
        if (string.IsNullOrWhiteSpace(failureReason))
        {
            throw new ArgumentException("A failed attempt must carry a reason (FR-014).", nameof(failureReason));
        }

        return new ActionAttempt(Require(kind), key, targetLabel, argumentsJson ?? "{}", false, failureReason);
    }

    private static string Require(string kind) =>
        string.IsNullOrWhiteSpace(kind)
            ? throw new ArgumentException("An attempt must name the capability kind it ran.", nameof(kind))
            : kind;
}
