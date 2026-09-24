using AskLucy.Application.Conversations.Runtime;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Runtime;

/// <summary>
/// specs/068 T043 — the supported retry phrasings, fixed as a named set so SC-003 and SC-010's
/// "at least 95%" becomes something that can actually pass or fail.
///
/// <para>
/// Both criteria are written about routing accuracy, and an accuracy figure with no fixture behind
/// it is a sentence rather than a target. <see cref="RetryPhrasing"/> is the deterministic floor
/// under the router: everything in <see cref="Supported"/> is recognised without a model call, so
/// the measured rate cannot drift when a provider changes a model behind the same name.
/// </para>
/// </summary>
public sealed class RetryPhrasingRoutingTests
{
    /// <summary>The phrasings tasks.md names, plus the near neighbours users actually type.</summary>
    private static readonly string[] SupportedPhrasings =
    [
        "try again",
        "Try again.",
        "TRY AGAIN!",
        "retry that",
        "retry",
        "do it again",
        "can you try once more",
        "could you try again please",
        "please try again",
        "try it again",
        "run it again",
        "attempt that again",
        "try the same again",
        "just try again",
        "try again one more time",
    ];

    public static TheoryData<string> Supported() => [.. SupportedPhrasings];

    /// <summary>
    /// Messages that must NOT be routed as a bare retry. Each one either carries new information
    /// (which replaying the recorded arguments would silently discard) or is not a retry at all.
    /// </summary>
    public static TheoryData<string> NotARetry() =>
    [
        "try again with the other entrance",           // new information the replay would drop
        "show me Al Safa Park 2 again",                // names its own target
        "why did that fail?",                          // a question about the failure, not a retry
        "do it",                                       // an instruction, with no "again" at all
        "what happened?",
        "try a different provider",
        "again",                                       // too little to act on: ask rather than guess
        "",
        "   ",
    ];

    [Theory]
    [MemberData(nameof(Supported))]
    public void ASupportedPhrasing_ShouldBeRecognisedAsARetry(string message) =>
        RetryPhrasing.IsRetryRequest(message).Should().BeTrue();

    [Theory]
    [MemberData(nameof(NotARetry))]
    public void AMessageCarryingAnythingElse_ShouldNotBeRecognised(string message) =>
        // FR-012 — the cost of a false positive is silently running the wrong thing with the wrong
        // arguments, which is strictly worse than handing the turn to the router to read properly.
        RetryPhrasing.IsRetryRequest(message).Should().BeFalse();

    [Fact]
    public void TheSupportedSet_ShouldRouteAtOrAbove95Percent()
    {
        var recognised = SupportedPhrasings.Count(RetryPhrasing.IsRetryRequest);

        // SC-003/SC-010, stated as the arithmetic they describe. Deterministic matching makes this
        // 100%; the threshold is asserted anyway so a future loosening of the pattern that trades
        // recall for reach has to face the number.
        ((double)recognised / SupportedPhrasings.Length).Should().BeGreaterThanOrEqualTo(0.95);
    }

    [Fact]
    public void NullMessage_ShouldNotBeRecognised() =>
        RetryPhrasing.IsRetryRequest(null).Should().BeFalse();
}
