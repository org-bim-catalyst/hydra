using AskLucy.Application.Conversations.Runtime;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Runtime;

/// <summary>
/// specs/068 T025 – T027 — the claim gate's behaviour table from contracts/turn-outcome.md §3,
/// plus the two guarantees that stop the cure being worse than the disease: a replacement is always
/// a whole sentence (FR-002b), and claim-free text is never held (SC-001b).
/// </summary>
public sealed class TurnOutcomeClaimGateTests
{
    private const string ResolveLocation = "resolve_location";
    private const string ResolveBoundary = "resolve_site_boundary";

    private static ActionAttempt Attempt(string key, bool succeeded, string? failureReason = null) => succeeded
        ? ActionAttempt.Success("Capability", key, "Al Safa Park 2", "{}")
        : ActionAttempt.Failure("Capability", key, "Al Safa Park 2", "{}",
            failureReason ?? "The AI provider rejected the request.");

    private static RecordedTurnOutcome Acted(params ActionAttempt[] attempts) =>
        RecordedTurnOutcome.Acted(attempts, DateTimeOffset.UtcNow);

    /// <summary>Streams a whole reply through the gate the way the controller does, one delta then the outcome.</summary>
    private static string Run(string reply, RecordedTurnOutcome? outcome)
    {
        var gate = new TurnOutcomeClaimGate();
        var released = gate.Accept(reply);
        if (outcome is not null)
        {
            released += gate.OutcomeRecorded(outcome);
        }

        return released + gate.Flush();
    }

    // ---- T025: the behaviour table ----

    [Theory]
    [InlineData("Al Safa Park is a public park in Dubai. It covers about 64 hectares.")]
    [InlineData("There are three options worth considering here.")]
    [InlineData("I can show you that if you'd like.")]
    [InlineData("I'll open the viewer once you confirm.")]
    public void ASentenceWithNoActionClaim_ShouldBeReleasedUnchanged(string reply)
    {
        // Including the conditional and future forms: "I can show you" and "I'll open" state an
        // intention, not a result, and a gate that swallowed those would make Lucy unable to offer.
        Run(reply, RecordedTurnOutcome.AnsweredInWords(DateTimeOffset.UtcNow)).Should().Be(reply);
    }

    [Fact]
    public void AClaimBackedByASuccessfulAttempt_ShouldBeReleasedByteIdentical()
    {
        const string reply = "I've shown you Al Safa Park 2 on the map.";

        // The false-positive guard. A correct reply must survive verification completely untouched —
        // punctuation, spacing and all — or the gate would degrade every turn it was meant to protect.
        Run(reply, Acted(Attempt(ResolveLocation, succeeded: true))).Should().Be(reply);
    }

    [Fact]
    public void AClaimContradictedByARecordedFailure_ShouldBeWithheldAndCorrected()
    {
        const string reply = "I've shown you Al Safa Park 2 on the map.";

        var released = Run(reply, Acted(Attempt(ResolveLocation, succeeded: false, "the location service was unavailable")));

        // The reported defect, reproduced at the unit level.
        released.Should().NotContain("I've shown you");
        released.Should().Contain("couldn't complete");
        released.Should().Contain("the location service was unavailable",
            "FR-002a — the correction names the recorded reason rather than a generic apology");
    }

    [Fact]
    public void AClaimWithNoMatchingAttempt_ShouldBeWithheld()
    {
        const string reply = "I've highlighted the site boundary for you.";

        // resolve_location succeeded; nothing outlined a boundary. Naming a capability the turn
        // never attempted is still a false claim, and the general "something worked" fallback must
        // not rescue it (FR-006).
        var released = Run(reply, Acted(Attempt(ResolveLocation, succeeded: true)));

        released.Should().NotContain("I've highlighted");
        released.Should().Contain("I can't confirm");
    }

    [Fact]
    public void AClaimWithNoRetrievableOutcome_ShouldBeWithheld()
    {
        const string reply = "I've shown you Al Safa Park 2 on the map.";

        // FR-002c — the turn ended with no outcome at all, which is what a failed persist looks
        // like. Unverified is treated as unproven, never as true.
        var released = Run(reply, outcome: null);

        released.Should().NotContain("I've shown you");
        released.Should().Contain("I can't confirm that actually happened");
    }

    [Fact]
    public void PartialSuccess_ShouldReleaseTheHalfThatWorked_AndCorrectTheHalfThatDidNot()
    {
        const string reply = "I've shown you Al Safa Park 2. I've highlighted the site boundary too.";

        var released = Run(reply, Acted(
            Attempt(ResolveLocation, succeeded: true),
            Attempt(ResolveBoundary, succeeded: false, "the boundary data could not be reached")));

        // FR-006 — reported per part. A single aggregate verdict would either suppress a true
        // statement or release a false one; both are failures of the same requirement.
        released.Should().StartWith("I've shown you Al Safa Park 2.");
        released.Should().NotContain("I've highlighted");
        released.Should().Contain("the boundary data could not be reached");
    }

    // ---- T026: a replacement is a whole sentence, never a gap ----

    [Theory]
    [InlineData(null)]
    [InlineData("The AI provider rejected the request.")]
    [InlineData("   ")]
    public void AWithheldSentence_ShouldBeReplacedByACompleteSentence(string? failureReason)
    {
        var outcome = failureReason is null
            ? null
            : Acted(ActionAttempt.Failure("Capability", ResolveLocation, "Al Safa Park 2", "{}",
                string.IsNullOrWhiteSpace(failureReason) ? "x" : failureReason));

        var released = Run("I've shown you Al Safa Park 2.", outcome);

        // FR-002b — never empty, never blank, never a fragment. A reply that simply stops reads as
        // a dropped connection, which is the failure mode specs/044 already had to fix once.
        released.Trim().Should().NotBeEmpty();
        released.Trim().Should().EndWith(".");
        released.Trim().Should().MatchRegex(@"^[A-Z].*");
    }

    [Fact]
    public void AWithheldSentence_ShouldKeepTheSpacingAroundIt()
    {
        var released = Run("I've shown you the site. Anything else?", Acted(Attempt(ResolveLocation, succeeded: false)));

        // The correction takes the withheld sentence's place in the stream, trailing space included,
        // so the sentence after it does not end up glued to it.
        released.Should().EndWith("Anything else?");
        released.Should().NotContain("..");
        released.Should().MatchRegex(@"\.\s+Anything else\?$");
    }

    // ---- T027: bounded buffering ----

    [Fact]
    public void ClaimFreeText_ShouldBeReleasedAtItsOwnSentenceBoundary()
    {
        var gate = new TurnOutcomeClaimGate();

        gate.Accept("Al Safa Park is a public park.").Should().Be("Al Safa Park is a public park.");
        gate.IsHolding.Should().BeFalse();

        // SC-001b — never held past the terminating boundary, and never waiting on an outcome that
        // only arrives at the end of the turn. The user watches claim-free prose stream as before.
        gate.Accept(" It covers 64 hectares.").Should().Be(" It covers 64 hectares.");
        gate.IsHolding.Should().BeFalse();
    }

    [Fact]
    public void AnIncompleteSentence_ShouldBeHeldOnlyUntilItsBoundaryArrives()
    {
        var gate = new TurnOutcomeClaimGate();

        gate.Accept("Al Safa Park is").Should().BeEmpty();
        gate.Accept(" a public park.").Should().Be("Al Safa Park is a public park.");
    }

    [Fact]
    public void AVeryLongUnpunctuatedRun_ShouldStillBeReleased()
    {
        var gate = new TurnOutcomeClaimGate();

        // Matches SentenceSegmenter's own escape hatch: markdown headings and bullet items often
        // carry no terminal punctuation at all, and waiting for one would stall the whole reply.
        var released = gate.Accept(string.Join(' ', Enumerable.Repeat("boundary", 40)));

        released.Should().NotBeEmpty();
        gate.IsHolding.Should().BeFalse();
    }

    [Fact]
    public void AClaimSentence_ShouldBeHeldUntilTheOutcomeArrives_AndNoFurther()
    {
        var gate = new TurnOutcomeClaimGate();

        gate.Accept("I've shown you Al Safa Park 2. ").Should().BeEmpty();
        gate.IsHolding.Should().BeTrue();

        var released = gate.OutcomeRecorded(Acted(Attempt(ResolveLocation, succeeded: true)));

        released.Should().Be("I've shown you Al Safa Park 2. ");
        gate.IsHolding.Should().BeFalse();
    }

    [Fact]
    public void TextAfterAHeldClaim_ShouldKeepItsOrder()
    {
        var gate = new TurnOutcomeClaimGate();

        gate.Accept("I've shown you the park. Let me know if you need anything else.").Should().BeEmpty();
        var released = gate.OutcomeRecorded(Acted(Attempt(ResolveLocation, succeeded: true)));

        // A claim-free sentence behind a held one queues rather than overtaking it: releasing it
        // first would reorder the reply, which is a worse defect than the delay it avoids.
        released.Should().Be("I've shown you the park. Let me know if you need anything else.");
    }

    [Fact]
    public void Flush_ShouldReleaseATrailingSentenceThatNeverGotItsFullStop()
    {
        var gate = new TurnOutcomeClaimGate();

        gate.Accept("Here is what I found").Should().BeEmpty();

        gate.Flush().Should().Be("Here is what I found");
    }
}
