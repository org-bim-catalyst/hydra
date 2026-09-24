using AskLucy.Application.Conversations.Runtime;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Runtime;

/// <summary>
/// specs/068 T007 — the invariants of the recorded outcome (data-model.md §2).
///
/// <para>
/// These rules exist because of a specific production failure: a turn died on a dead provider
/// credential, nothing recorded that it had died, and a later turn read the leftover prose as a
/// success. Every rule below removes one way for an outcome to be recorded that does not say what
/// actually happened.
/// </para>
/// </summary>
public sealed class RecordedTurnOutcomeTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    private static ActionAttempt SucceededLocation(string target = "Al Safa Park 2") =>
        ActionAttempt.Success("resolve_location", "resolve_location", target, """{"query":"Al Safa Park 2"}""");

    private static ActionAttempt FailedLocation(string reason = "Provider credential rejected") =>
        ActionAttempt.Failure("resolve_location", "resolve_location", "Al Safa Park 2", """{"query":"Al Safa Park 2"}""", reason);

    [Fact]
    public void AnsweredInWords_ShouldRecordNoAttempts()
    {
        var outcome = RecordedTurnOutcome.AnsweredInWords(At);

        outcome.Verdict.Should().Be(TurnVerdict.AnsweredInWords);
        outcome.Attempts.Should().BeEmpty();
        outcome.FailureReason.Should().BeNull();
        outcome.RecordedAtUtc.Should().Be(At);
    }

    [Fact]
    public void AnsweredInWords_ShouldSupportNoSuccessClaim()
    {
        RecordedTurnOutcome.AnsweredInWords(At).SupportsAnySuccessClaim.Should().BeFalse();
    }

    [Fact]
    public void Acted_ShouldRequireAtLeastOneAttempt()
    {
        var act = () => RecordedTurnOutcome.Acted([], At);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Acted_ShouldKeepEveryAttemptsOwnResult()
    {
        var outcome = RecordedTurnOutcome.Acted([SucceededLocation(), FailedLocation()], At);

        outcome.Verdict.Should().Be(TurnVerdict.Acted);
        outcome.Attempts.Should().HaveCount(2);
        outcome.Attempts[0].Succeeded.Should().BeTrue();
        outcome.Attempts[1].Succeeded.Should().BeFalse();
    }

    [Fact]
    public void FailedBeforeCompleting_ShouldRequireAReason()
    {
        var act = () => RecordedTurnOutcome.FailedBeforeCompleting("  ", At);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FailedBeforeCompleting_ShouldRecordTheReasonAndNoAttemptsByDefault()
    {
        var outcome = RecordedTurnOutcome.FailedBeforeCompleting("Provider credential rejected", At);

        outcome.Verdict.Should().Be(TurnVerdict.FailedBeforeCompleting);
        outcome.FailureReason.Should().Be("Provider credential rejected");
        outcome.Attempts.Should().BeEmpty();
        outcome.SupportsAnySuccessClaim.Should().BeFalse();
    }

    [Fact]
    public void FailedBeforeCompleting_ShouldKeepThePartThatWorked()
    {
        // The spec's partial-success edge case: location resolved, boundary outlining then died.
        var outcome = RecordedTurnOutcome.FailedBeforeCompleting(
            "Boundary service unavailable",
            At,
            [SucceededLocation()]);

        outcome.Attempts.Should().ContainSingle().Which.Succeeded.Should().BeTrue();
        outcome.SupportsSuccessClaimFor("resolve_location").Should().BeTrue();
        outcome.SupportsSuccessClaimFor("resolve_site_boundary").Should().BeFalse();
    }

    [Fact]
    public void FailedAttempt_ShouldRequireAReason()
    {
        var act = () => ActionAttempt.Failure("resolve_location", null, null, "{}", "");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Attempt_ShouldRequireACapabilityKind()
    {
        var act = () => ActionAttempt.Success(" ", null, null, "{}");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SupportsSuccessClaimFor_ShouldIgnoreAFailedAttemptOfTheSameKind()
    {
        // The reported defect, reduced: the turn tried to show the location and could not.
        // Nothing here may be read as "I've shown you Al Safa Park 2."
        var outcome = RecordedTurnOutcome.Acted([FailedLocation()], At);

        outcome.SupportsSuccessClaimFor("resolve_location").Should().BeFalse();
        outcome.SupportsAnySuccessClaim.Should().BeFalse();
    }

    [Fact]
    public void SupportsSuccessClaimFor_ShouldMatchKindCaseInsensitively()
    {
        var outcome = RecordedTurnOutcome.Acted([SucceededLocation()], At);

        outcome.SupportsSuccessClaimFor("Resolve_Location").Should().BeTrue();
    }
}
