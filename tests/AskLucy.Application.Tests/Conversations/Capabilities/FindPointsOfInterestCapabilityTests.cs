using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Locations;
using AskLucy.Application.PointsOfInterest;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Capabilities;

/// <summary>
/// specs/090-poi-discovery — the category search must ask before replacing an existing layer,
/// must honour keep/replace on the answer, and must never report an Overpass outage as
/// "no places found".
/// </summary>
public sealed class FindPointsOfInterestCapabilityTests
{
    private static readonly Guid ChatId = Guid.NewGuid();

    private readonly ILocationResolutionService _locationService = Substitute.For<ILocationResolutionService>();
    private readonly IPoiQueryProvider _poiProvider = Substitute.For<IPoiQueryProvider>();
    private readonly IUserChatRepository _chats = Substitute.For<IUserChatRepository>();
    private readonly IMessageRepository _messages = Substitute.For<IMessageRepository>();
    private readonly PoiCategoryCatalog _catalog = new();
    private readonly PoiLayerStateStore _store;

    private FindPointsOfInterestCapability Build() =>
        new(_locationService, _poiProvider, _catalog, _store, _chats, _messages);

    public FindPointsOfInterestCapabilityTests()
    {
        _store = new PoiLayerStateStore(TimeProvider.System, _catalog);
    }

    private static AgentToolExecutionContext Context() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "user-1", Guid.NewGuid(), Guid.NewGuid(), ChatId);

    private void GivenAreaResolvesTo(string area, string name = "Muscat")
    {
        _locationService.ResolveQueryAsync(ChatId, area, Arg.Any<CancellationToken>())
            .Returns(new LocationResolutionOutcome(
                LocationResolutionOutcomeType.Confirmed,
                new ConfirmedLocationData(23.588, 58.382, name, 0.9, Viewport: new ViewportBounds(23.65, 58.45, 23.52, 58.31)),
                null));
    }

    private void GivenPois(params string[] names)
    {
        _poiProvider.QueryAsync(Arg.Any<PoiQuery>(), Arg.Any<CancellationToken>())
            .Returns([.. names.Select((n, i) => new PoiFeature($"osm_node_{i}", n, 23.5 + i, 58.3 + i, new Dictionary<string, string>()))]);
    }

    private static JsonDocument Input(string json) => JsonDocument.Parse(json);

    [Fact]
    public async Task ExecuteAsync_ShouldTreatABareYesAsReplace_InsteadOfAskingAgain()
    {
        GivenAreaResolvesTo("Muscat");
        GivenPois("School A");
        var capability = Build();
        await capability.ExecuteAsync(Context(), Input("""{"category":"schools","area":"Muscat"}"""), default);
        await capability.ExecuteAsync(Context(), Input("""{"category":"hospitals","area":"Muscat"}"""), default);

        // The decider never saw the Tier 2 guidance that says to pass mode, so it sent only the
        // category back; the user's own "yes" is the answer the question asked for.
        _messages.ListByChatIdAsync(ChatId, Arg.Any<CancellationToken>())
            .Returns([Domain.Chats.Message.Create(
                ChatId, Domain.Chats.MessageRole.User, Domain.Chats.MessageKind.Text,
                "yes", null, "user")]);

        var result = await capability.ExecuteAsync(Context(), Input("""{"category":"hospitals"}"""), default);

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.GetProperty("mode").GetString().Should().Be("replace");
        _store.GetLayers(ChatId).Select(l => l.Key).Should().Equal("hospital");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRecoverTheCategoryFromTheUserMessage_WhenTheModelOmittedIt()
    {
        // Observed live: "show me all schools in Oman" reached the capability as {"area":"Oman"}.
        GivenAreaResolvesTo("Oman", "Oman");
        GivenPois("School A");
        _messages.ListByChatIdAsync(ChatId, Arg.Any<CancellationToken>())
            .Returns([Domain.Chats.Message.Create(
                ChatId, Domain.Chats.MessageRole.User, Domain.Chats.MessageKind.Text,
                "show me all schools in Oman", null, "user")]);

        var result = await Build().ExecuteAsync(Context(), Input("""{"area":"Oman"}"""), default);

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.GetProperty("category").GetString().Should().Be("school");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectACategoryItCannotMap_AndNameTheSupportedOnes()
    {
        var result = await Build().ExecuteAsync(Context(), Input("""{"category":"spaceports","area":"Muscat"}"""), default);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("school").And.Contain("hospital").And.Contain("mosque");
        await _poiProvider.DidNotReceiveWithAnyArgs().QueryAsync(default!, default);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldDrawWithoutAsking_WhenNothingIsOnTheMapYet()
    {
        GivenAreaResolvesTo("Muscat");
        GivenPois("School A", "School B");

        var result = await Build().ExecuteAsync(Context(), Input("""{"category":"schools","area":"Muscat"}"""), default);

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.GetProperty("count").GetInt32().Should().Be(2);
        result.Output.RootElement.GetProperty("mode").GetString().Should().Be("replace");
        result.Output.RootElement.GetProperty("markers").GetArrayLength().Should().Be(2);
        _store.GetLayers(ChatId).Select(l => l.Key).Should().Equal("school");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldAskBeforeReplacing_WhenAnotherCategoryIsAlreadyDrawn()
    {
        GivenAreaResolvesTo("Muscat");
        GivenPois("School A");
        var capability = Build();
        await capability.ExecuteAsync(Context(), Input("""{"category":"schools","area":"Muscat"}"""), default);

        // The second request carries no mode: nothing may be searched or drawn before the answer.
        _poiProvider.ClearReceivedCalls();
        var result = await capability.ExecuteAsync(
            Context(), Input("""{"category":"hospitals","area":"Muscat"}"""), default);

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.GetProperty("awaitingChoice").GetBoolean().Should().BeTrue();
        result.Output.RootElement.GetProperty("question").GetString()
            .Should().Contain("schools").And.Contain("hospitals");
        result.Output.RootElement.TryGetProperty("markers", out _).Should().BeFalse();
        await _poiProvider.DidNotReceiveWithAnyArgs().QueryAsync(default!, default);
        _store.GetLayers(ChatId).Select(l => l.Key).Should().Equal("school");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldKeepBothLayers_WhenTheAnswerIsKeep()
    {
        GivenAreaResolvesTo("Muscat");
        GivenPois("School A");
        var capability = Build();
        await capability.ExecuteAsync(Context(), Input("""{"category":"schools","area":"Muscat"}"""), default);
        await capability.ExecuteAsync(Context(), Input("""{"category":"hospitals","area":"Muscat"}"""), default);

        // "keep both" — category and mode only; the pending request remembers the area.
        var result = await capability.ExecuteAsync(
            Context(), Input("""{"category":"hospitals","mode":"keep"}"""), default);

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.GetProperty("mode").GetString().Should().Be("keep");
        _store.GetLayers(ChatId).Select(l => l.Key).Should().BeEquivalentTo(["school", "hospital"]);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReplaceEveryLayer_WhenTheAnswerIsReplace()
    {
        GivenAreaResolvesTo("Muscat");
        GivenPois("School A");
        var capability = Build();
        await capability.ExecuteAsync(Context(), Input("""{"category":"schools","area":"Muscat"}"""), default);
        await capability.ExecuteAsync(Context(), Input("""{"category":"hospitals","area":"Muscat"}"""), default);

        var result = await capability.ExecuteAsync(
            Context(), Input("""{"category":"hospitals","mode":"replace"}"""), default);

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.GetProperty("mode").GetString().Should().Be("replace");
        _store.GetLayers(ChatId).Select(l => l.Key).Should().Equal("hospital");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReportAnEmptyAreaAsZero_NotAsAFailure()
    {
        GivenAreaResolvesTo("Muscat");
        _poiProvider.QueryAsync(Arg.Any<PoiQuery>(), Arg.Any<CancellationToken>()).Returns([]);

        var result = await Build().ExecuteAsync(Context(), Input("""{"category":"schools","area":"Muscat"}"""), default);

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.GetProperty("count").GetInt32().Should().Be(0);
        result.Output.RootElement.GetProperty("markers").GetArrayLength().Should().Be(0);
        _store.GetLayers(ChatId).Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReportAnOutageAsAFailure_NotAsZeroResults()
    {
        GivenAreaResolvesTo("Muscat");
        _poiProvider.QueryAsync(Arg.Any<PoiQuery>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<PoiFeature>>(_ => throw new BoundaryProviderUnavailableException("all endpoints down"));

        var result = await Build().ExecuteAsync(Context(), Input("""{"category":"schools","area":"Muscat"}"""), default);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("unavailable");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFailTruthfully_WhenTheAreaCannotBeResolved()
    {
        _locationService.ResolveQueryAsync(ChatId, "Atlantis", Arg.Any<CancellationToken>())
            .Returns(new LocationResolutionOutcome(
                LocationResolutionOutcomeType.NotFound, null, "I couldn't find Atlantis."));

        var result = await Build().ExecuteAsync(Context(), Input("""{"category":"schools","area":"Atlantis"}"""), default);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Be("I couldn't find Atlantis.");
        await _poiProvider.DidNotReceiveWithAnyArgs().QueryAsync(default!, default);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSearchTheResolvedAreaExtent_NotACircleAroundItsCentre()
    {
        GivenAreaResolvesTo("Oman", "Oman");
        PoiQuery? captured = null;
        _poiProvider.QueryAsync(Arg.Do<PoiQuery>(q => captured = q), Arg.Any<CancellationToken>()).Returns([]);

        await Build().ExecuteAsync(Context(), Input("""{"category":"hospitals","area":"Oman"}"""), default);

        captured.Should().NotBeNull();
        captured!.Bounds.Should().Be(new PoiBoundingBox(23.52, 58.31, 23.65, 58.45));
    }

    [Fact]
    public void Sync_ShouldForgetACategoryTheClientRemoved()
    {
        _store.ApplyLayer(ChatId, _catalog.FindByKey("school")!, keepExisting: false);
        _store.ApplyLayer(ChatId, _catalog.FindByKey("hospital")!, keepExisting: true);

        // The user closed the hospitals row in the legend; the next turn reports only schools.
        _store.Sync(ChatId, [new ActivePoiCategoryInput("school", "Schools")]);

        _store.GetLayers(ChatId).Select(l => l.Key).Should().Equal("school");
    }

    [Fact]
    public void WhenToUse_ShouldExplicitlyExcludeSingleNamedPlaces()
    {
        var capability = Build();

        capability.WhenToUse.Should().Contain("Never for one named place");
        capability.WhenToUse.Should().Contain("resolve_location");
    }
}
