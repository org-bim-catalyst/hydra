using AskLucy.Application.Conversations.Runtime;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Runtime;

/// <summary>
/// The offer loop reported in T070: Site Analysis offered Sunlight, Sunlight offered Site Analysis
/// again, and a user clicking through the offers never ran out of them.
/// <see cref="ActiveSiteHistory"/> is what tells the offer step both have already run here.
/// </summary>
public sealed class ActiveSiteHistoryTests
{
    private static RecordedTurnOutcome Succeeded(params string[] keys) =>
        RecordedTurnOutcome.Acted([.. keys.Select(k => ActionAttempt.Success("Capability", k, null, "{}"))], DateTimeOffset.UtcNow);

    private static RecordedTurnOutcome Failed(string key) =>
        RecordedTurnOutcome.Acted([ActionAttempt.Failure("Capability", key, null, "{}", "the service was unavailable")], DateTimeOffset.UtcNow);

    [Fact]
    public void CapabilitiesThatRanForTheCurrentSite_ShouldAllCount()
    {
        var keys = ActiveSiteHistory.CompletedCapabilityKeys(
        [
            Succeeded("resolve_location", "resolve_site_boundary"),
            Succeeded("request_site_analysis"),
            RecordedTurnOutcome.AnsweredInWords(DateTimeOffset.UtcNow),
            Succeeded("open_solar_analysis"),
        ]);

        keys.Should().BeEquivalentTo("resolve_site_boundary", "request_site_analysis", "open_solar_analysis");
    }

    [Fact]
    public void ANewerLocation_ShouldStartTheRecordAgain()
    {
        // What ran for the previous site is worth offering again for the new one.
        var keys = ActiveSiteHistory.CompletedCapabilityKeys(
        [
            Succeeded("resolve_location"),
            Succeeded("request_site_analysis"),
            Succeeded("resolve_location"),
            Succeeded("open_solar_analysis"),
        ]);

        keys.Should().Equal("open_solar_analysis");
    }

    [Fact]
    public void AFailedAttempt_ShouldNotCount()
    {
        // A capability that failed is exactly one the user may still want offered.
        var keys = ActiveSiteHistory.CompletedCapabilityKeys([Succeeded("resolve_location"), Failed("request_site_analysis")]);

        keys.Should().BeEmpty();
    }

    [Fact]
    public void AFailedLocationResolution_ShouldNotMoveTheSite()
    {
        var keys = ActiveSiteHistory.CompletedCapabilityKeys(
            [Succeeded("resolve_location"), Succeeded("request_site_analysis"), Failed("resolve_location")]);

        keys.Should().Equal("request_site_analysis");
    }

    [Fact]
    public void UnreadableOutcomes_ShouldBeSkipped()
    {
        var keys = ActiveSiteHistory.CompletedCapabilityKeys([null, Succeeded("open_solar_analysis"), null]);

        keys.Should().Equal("open_solar_analysis");
    }
}
