using System.Text.Json;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Runtime;
using AskLucy.Domain.Conversations;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Runtime;

/// <summary>
/// specs/083 — the hand-composed "a picture, or a map move?" card. Its two rows are the whole
/// point: one returns an image, the other moves the viewer's camera, and each must carry
/// arguments its own capability will accept.
/// </summary>
public sealed class MapSnapshotOfferTests
{
    [Fact]
    public void Build_OffersTheSnapshotFirst_ThenTheMapMove_ThenTheDecline()
    {
        var offer = MapSnapshotOffer.Build("Al Safa Park 2");

        offer.Actions.Should().HaveCount(3);
        offer.Actions[0].Kind.Should().Be(SuggestedActionKind.Capability);
        offer.Actions[0].Key.Should().Be(MapSnapshotCapability.CapabilityKey);
        offer.Actions[1].Kind.Should().Be(SuggestedActionKind.Capability);
        offer.Actions[1].Key.Should().Be(ResolveLocationCapability.CapabilityKey);
        offer.Actions[^1].Kind.Should().Be(SuggestedActionKind.Decline);
    }

    [Fact]
    public void Build_GivesEachRowArgumentsItsOwnCapabilityWillAccept()
    {
        var offer = MapSnapshotOffer.Build("Al Safa Park 2");

        // The snapshot capability reads the place under "location".
        using var snapshot = JsonDocument.Parse(offer.Actions[0].ArgumentsJson!);
        snapshot.RootElement.GetProperty("location").GetString().Should().Be("Al Safa Park 2");

        // resolve_location's input schema requires a "query" — a row with no arguments would be
        // refused at dispatch.
        using var move = JsonDocument.Parse(offer.Actions[1].ArgumentsJson!);
        move.RootElement.GetProperty("query").GetString().Should().Be("Al Safa Park 2");
    }

    [Fact]
    public void Build_AsksTheQuestionTheTwoAnswersAnswer()
    {
        var offer = MapSnapshotOffer.Build("Al Safa Park 2");

        offer.Question.Should().Contain("Al Safa Park 2");
        offer.Question.Should().Contain("picture");
        offer.Question.Should().Contain("map");
    }

    [Fact]
    public void Build_ProducesStructurallyValidRows() =>
        MapSnapshotOffer.Build("Al Safa Park 2").Actions.Should().OnlyContain(a => a.IsStructurallyValid());
}
