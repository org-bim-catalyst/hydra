using System.Text.Json;
using AskLucy.Application.Conversations;
using AskLucy.Domain.Conversations;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Conversations;

/// <summary>
/// specs/068 — the offer wire shape, asserted as the closed round trip it has to be. Every
/// previous test of this crossing point either wrote a shape production does not write or
/// substituted the reader outright, which is why two independent drifts shipped unnoticed: the
/// kind token (<c>"Capability"</c> written, <c>"capability"</c> parsed, every selection 500'd) and
/// the row shape (<c>capabilityKey</c> written, <c>Key</c> read, so no row could ever match).
/// </summary>
public sealed class SuggestedActionWireTests
{
    private static SuggestedActionOffer Offer() => new(
        "Would you like me to do any of these?",
        [
            new SuggestedAction(SuggestedActionKind.Capability, "request_site_analysis", null, "Analyze this site", "Runs the site analysis.", """{"siteName":"Al Safa Park 2"}"""),
            new SuggestedAction(SuggestedActionKind.FollowUp, null, "Tell me about the surroundings", "The surroundings", "More about the area.", null),
            SuggestedAction.Decline(),
        ]);

    /// <summary>
    /// The assertion the whole type exists for: what the client is shown is what the resolver
    /// reads back, field for field, with nothing lost in between.
    /// </summary>
    [Fact]
    public void SerializeThenDeserialize_ShouldPreserveEveryRow_Exactly()
    {
        var restored = SuggestedActionWire.TryDeserialize(SuggestedActionWire.Serialize(Offer()));

        restored.Should().NotBeNull();
        restored!.Question.Should().Be("Would you like me to do any of these?");
        restored.Actions.Should().HaveCount(3);

        var capability = restored.Actions[0];
        capability.Kind.Should().Be(SuggestedActionKind.Capability);
        capability.Key.Should().Be("request_site_analysis", "capabilityKey on the wire is Key in the domain");
        capability.Label.Should().Be("Analyze this site");
        capability.Description.Should().Be("Runs the site analysis.");
        capability.ArgumentsJson.Should().Be("""{"siteName":"Al Safa Park 2"}""");

        restored.Actions[1].Kind.Should().Be(SuggestedActionKind.FollowUp);
        restored.Actions[1].Text.Should().Be("Tell me about the surroundings");
        restored.Actions[1].ArgumentsJson.Should().BeNull();

        restored.Actions[2].IsDecline.Should().BeTrue();
    }

    [Fact]
    public void Serialize_ShouldUseTheContractsCamelCaseFieldNames()
    {
        // contracts/turn-stream.md 2 and contracts/suggested-actions-api.md 1. The frontend's
        // toOfferFields reads exactly these names; a rename here blanks every card on reload.
        var json = SuggestedActionWire.Serialize(Offer());

        json.Should().Contain("\"question\"")
            .And.Contain("\"actions\"")
            .And.Contain("\"kind\"")
            .And.Contain("\"capabilityKey\"")
            .And.Contain("\"label\"")
            .And.Contain("\"description\"")
            .And.Contain("\"isDecline\"");

        json.Should().NotContain("\"Key\"").And.NotContain("\"Label\"").And.NotContain("\"IsDecline\"");
    }

    [Fact]
    public void Serialize_ShouldWriteArgumentsAsAnObject_NotAnEscapedString()
    {
        var actions = JsonDocument.Parse(SuggestedActionWire.Serialize(Offer())).RootElement.GetProperty("actions");

        actions[0].GetProperty("arguments").ValueKind.Should().Be(JsonValueKind.Object);
        actions[1].GetProperty("arguments").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData(SuggestedActionKind.FlowVariant, "flowVariant")]
    [InlineData(SuggestedActionKind.Capability, "capability")]
    [InlineData(SuggestedActionKind.FollowUp, "followUp")]
    [InlineData(SuggestedActionKind.Decline, "decline")]
    public void ToWire_ShouldEmitTheContractToken_NotTheEnumName(SuggestedActionKind kind, string expected)
    {
        // Pinned against the contract as literals on purpose. Kind.ToString() passes a
        // case-insensitive comparison and still breaks a client matching on the exact token.
        SuggestedActionWire.ToWire(kind).Should().Be(expected);
    }

    [Fact]
    public void ToWire_ShouldRefuseAKindItDoesNotKnow_RatherThanGuess()
    {
        // A kind added to the enum with no token here is a card the client cannot classify; the
        // throw makes that a failing test rather than a silent runtime shrug.
        var act = () => SuggestedActionWire.ToWire((SuggestedActionKind)99);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("capability")]
    [InlineData("Capability")]
    [InlineData("CAPABILITY")]
    public void TryParseKind_ShouldAcceptBothSpellings_SoOldOffersStayClickable(string token)
    {
        SuggestedActionWire.TryParseKind(token, out var parsed).Should().BeTrue();
        parsed.Should().Be(SuggestedActionKind.Capability);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("teleport")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryParseKind_ShouldRejectAnythingThatIsNotAToken_IncludingTheUnderlyingNumber(string? token)
    {
        // Enum.TryParse accepts "1" and would map a malformed client request onto a real kind.
        SuggestedActionWire.TryParseKind(token, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("""{"question":"x","actions":[]}""")]
    [InlineData("""{"question":"x","actions":[{"kind":"teleport","label":"Go"}]}""")]
    public void TryDeserialize_ShouldReturnNull_ForAnythingUnanswerable(string json)
    {
        // All four mean the same thing to the caller: this offer can no longer be answered, which
        // it reports as stale rather than as a crash mid-stream.
        SuggestedActionWire.TryDeserialize(json).Should().BeNull();
    }

    [Fact]
    public void TryDeserialize_ShouldStillReadAnOfferPersistedBeforeThisTypeExisted()
    {
        // The rows already in the database: enum-name kind, and no arguments object at all.
        const string legacy = """
            {"question":"Pick one","actions":[
              {"kind":"Capability","capabilityKey":"request_site_analysis","label":"Analyze this site"},
              {"kind":"Decline","label":"No thanks","isDecline":true}]}
            """;

        var restored = SuggestedActionWire.TryDeserialize(legacy);

        restored.Should().NotBeNull();
        restored!.Actions[0].Kind.Should().Be(SuggestedActionKind.Capability);
        restored.Actions[0].Key.Should().Be("request_site_analysis");
        restored.Actions[0].ArgumentsJson.Should().BeNull();
        restored.Actions[1].IsDecline.Should().BeTrue();
    }
}
