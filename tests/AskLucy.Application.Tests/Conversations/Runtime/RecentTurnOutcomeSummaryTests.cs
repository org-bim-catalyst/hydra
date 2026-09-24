using AskLucy.Application.Ai;
using AskLucy.Application.Conversations.Runtime;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Runtime;

/// <summary>
/// specs/068 FR-009, FR-009a – FR-009c, FR-006 — the routing summary that removes the model's
/// motive to fabricate, and the bound that stops it growing with the conversation (SC-009).
///
/// <para>
/// Written alongside T034/T036, which is where this projection is first used; T039 in Phase 4 is
/// the same contract from the retry side.
/// </para>
/// </summary>
public sealed class RecentTurnOutcomeSummaryTests
{
    private static RecordedTurnOutcome Succeeded(string key, string target) =>
        RecordedTurnOutcome.Acted([ActionAttempt.Success("Capability", key, target, "{}")], DateTimeOffset.UtcNow);

    private static RecordedTurnOutcome Failed(string key, string target, string reason) =>
        RecordedTurnOutcome.Acted([ActionAttempt.Failure("Capability", key, target, "{}", reason)], DateTimeOffset.UtcNow);

    [Fact]
    public void ASuccessfulTurn_ShouldBeReportedAsSucceeded()
    {
        var summary = RecentTurnOutcomeSummary.From([Succeeded("resolve_location", "Al Safa Park 2")]);

        var line = summary.Turns.Should().ContainSingle().Subject;
        line.Kind.Should().Be("resolve_location");
        line.TargetLabel.Should().Be("Al Safa Park 2");
        line.Verdict.Should().Be("succeeded");
    }

    [Fact]
    public void AFailedTurn_ShouldCarryItsReason()
    {
        var summary = RecentTurnOutcomeSummary.From([Failed("resolve_location", "Al Safa Park 2", "the provider was unavailable")]);

        var line = summary.Turns.Should().ContainSingle().Subject;
        line.Verdict.Should().Be("failed");
        line.FailureReason.Should().Be("the provider was unavailable");
    }

    [Fact]
    public void APartlySuccessfulTurn_ShouldProduceOneLinePerAttempt()
    {
        var summary = RecentTurnOutcomeSummary.From([RecordedTurnOutcome.Acted(
            [
                ActionAttempt.Success("Capability", "resolve_location", "Al Safa Park 2", "{}"),
                ActionAttempt.Failure("Capability", "resolve_site_boundary", "Al Safa Park 2", "{}", "no boundary data"),
            ],
            DateTimeOffset.UtcNow)]);

        // FR-006 — averaging these into one verdict destroys exactly the information the
        // requirement exists to preserve, and would have the composer either over- or under-claim.
        summary.Turns.Should().HaveCount(2);
        summary.Turns.Select(t => t.Verdict).Should().Equal("succeeded", "failed");
    }

    [Fact]
    public void ATurnThatDiedPartwayThrough_ShouldSayThat_AsWellAsWhatItManaged()
    {
        var summary = RecentTurnOutcomeSummary.From([RecordedTurnOutcome.FailedBeforeCompleting(
            "the service it needed could not be reached",
            DateTimeOffset.UtcNow,
            [ActionAttempt.Success("Capability", "resolve_location", "Al Safa Park 2", "{}")])]);

        summary.Turns.Select(t => t.Verdict).Should().Equal("succeeded", "did-not-complete");
    }

    [Fact]
    public void ATurnThatOnlyTalked_ShouldSaySo()
    {
        var summary = RecentTurnOutcomeSummary.From([RecordedTurnOutcome.AnsweredInWords(DateTimeOffset.UtcNow)]);

        summary.Turns.Should().ContainSingle().Which.Verdict.Should().Be("answered-only");
    }

    [Fact]
    public void TheSummary_ShouldStayConstantSized_AsTheConversationGrows()
    {
        var manyTurns = Enumerable.Range(0, 40)
            .Select(i => (RecordedTurnOutcome?)Succeeded("resolve_location", $"Place {i}"))
            .ToList();

        var summary = RecentTurnOutcomeSummary.From(manyTurns);

        // FR-009a/SC-009 — a prompt cost that grows with history is a prompt cost nobody notices
        // until a long conversation stops fitting.
        summary.Turns.Should().HaveCount(RecentTurnOutcomeSummary.MaxTurns);
        summary.Turns[^1].TargetLabel.Should().Be("Place 39", "the newest turns are the relevant ones");
    }

    [Fact]
    public void MissingOutcomes_ShouldBeSkipped_NotCountedAgainstTheCap()
    {
        var summary = RecentTurnOutcomeSummary.From([null, Succeeded("resolve_location", "Al Safa Park 2"), null]);

        summary.Turns.Should().ContainSingle();
    }

    [Fact]
    public void AnEmptySummary_ShouldLeaveThePromptExactlyAsItWas()
    {
        RecentTurnOutcomeSummary.Empty.ToPromptText().Should().BeEmpty();

        // FR-009c — a conversation with no recorded outcomes must get byte-identical framing to the
        // one it got before this feature existed, or every prompt-shape test becomes a guess.
        ReplyScopePromptFraming.BuildSystemMessage(RecentTurnOutcomeSummary.Empty)
            .Should().Be(ReplyScopePromptFraming.BuildSystemMessage());
    }

    [Fact]
    public void ANonEmptySummary_ShouldTellTheModelNotToClaimWhatDidNotHappen()
    {
        var text = RecentTurnOutcomeSummary
            .From([Failed("resolve_location", "Al Safa Park 2", "the provider was unavailable")])
            .ToPromptText();

        text.Should().Contain("resolve_location")
            .And.Contain("Al Safa Park 2")
            .And.Contain("the provider was unavailable")
            .And.Contain("Do not say an action was carried out unless it is listed above as succeeded");
    }
}
