using System.Text.Json;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Runtime;
using AskLucy.Domain.Conversations;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using Xunit;
using static AskLucy.Application.Tests.SiteBoundaries.BurJumanSite;

namespace AskLucy.Application.Tests.Conversations.Runtime;

/// <summary>specs/077 — the "which buildings does the site include?" question asked after a resolution.</summary>
public sealed class SiteBoundaryMembershipOfferTests
{
    [Fact]
    public void Build_OffersEachGroupInTurnThenOtherThenDecline()
    {
        var offer = SiteBoundaryMembershipOffer.Build(Boundary(Members))!;

        offer.Question.Should().Be("Which buildings should the BurJuman Mall site include?");
        offer.Actions.Select(a => a.Label).Should().Equal(
            "A. BurJuman Mall only",
            "B. BurJuman Mall with its connected buildings (shown now)",
            "C. Also the buildings across the street",
            "D. Everything, including the station",
            "E. Other — I'll name the buildings",
            "Keep the outline as it is");
        offer.Actions[^1].Kind.Should().Be(SuggestedActionKind.Decline);
        offer.Actions[^2].Kind.Should().Be(SuggestedActionKind.FollowUp);
        offer.Actions.Take(4).Should().OnlyContain(a =>
            a.Kind == SuggestedActionKind.Capability && a.Key == SetSiteBoundaryMembersCapability.CapabilityKey);
    }

    [Fact]
    public void Build_EachRowCarriesExactlyTheBuildingsItNames()
    {
        var offer = SiteBoundaryMembershipOffer.Build(Boundary(Members))!;

        offer.Actions.Take(4).Select(a => MemberIds(a.ArgumentsJson)).Should().BeEquivalentTo(
            new[]
            {
                Array.Empty<string>(),
                ["osm_way_1", "osm_way_2"],
                ["osm_way_1", "osm_way_2", "osm_way_3"],
                ["osm_way_1", "osm_way_2", "osm_way_3", "osm_way_4"],
            },
            options => options.WithStrictOrdering());
        offer.Actions[1].Description.Should().Contain("BurJuman Business Tower").And.Contain("BurJuman Arjaan by Rotana");
    }

    [Fact]
    public void Build_LettersFollowTheGroupsActuallyFound()
    {
        var nearbyOnly = Members.Where(m => m.Relation == SiteBoundaryMemberRelation.Nearby && m.Kind == SiteBoundaryMemberKind.Building)
            .Select(m => m with { Included = false }).ToList();

        var offer = SiteBoundaryMembershipOffer.Build(Boundary(nearbyOnly))!;

        offer.Actions.Select(a => a.Label).Should().Equal(
            "A. BurJuman Mall only (shown now)",
            "B. BurJuman Mall with its nearby buildings",
            "C. Other — I'll name the buildings",
            "Keep the outline as it is");
    }

    [Fact]
    public void Build_AsksNothingWhenNoBuildingWasFound() =>
        SiteBoundaryMembershipOffer.Build(Boundary()).Should().BeNull();

    [Fact]
    public void Build_EveryRowIsStructurallyValid() =>
        SiteBoundaryMembershipOffer.Build(Boundary(Members))!.Actions.Should().OnlyContain(a => a.IsStructurallyValid());

    private static string[] MemberIds(string? argumentsJson)
    {
        using var document = JsonDocument.Parse(argumentsJson!);
        return [.. document.RootElement.GetProperty("memberIds").EnumerateArray().Select(e => e.GetString()!)];
    }
}
