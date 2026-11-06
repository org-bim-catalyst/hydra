using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Locations;
using AskLucy.Application.Spatial;
using AskLucy.Application.Tests.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Spatial;

/// <summary>
/// specs/094 — resolution order is explicit place → active boundary → map view → ask, and a place
/// that cannot be found is a failure, not a follow-up question.
/// </summary>
public sealed class StudyAreaResolverTests
{
    private static readonly Guid ChatId = Guid.NewGuid();

    private readonly ILocationResolutionService _locations = Substitute.For<ILocationResolutionService>();
    private readonly StudyAreaResolver _resolver;

    public StudyAreaResolverTests()
    {
        _resolver = new StudyAreaResolver(
            _locations,
            Substitute.For<IUserChatRepository>(),
            TestEffectiveSiteBoundary.None());
    }

    private static MapViewportContext MapView => new(23.588, 58.382, 12, 23.52, 58.31, 23.65, 58.45);

    private void GivenConfirmed(string area, string name = "Muscat")
    {
        _locations.ResolveQueryAsync(ChatId, area, Arg.Any<CancellationToken>())
            .Returns(new LocationResolutionOutcome(
                LocationResolutionOutcomeType.Confirmed,
                new ConfirmedLocationData(
                    23.588, 58.382, name, 0.9,
                    Viewport: new ViewportBounds(23.65, 58.45, 23.52, 58.31)),
                null));
    }

    [Fact]
    public async Task ResolveAsync_ShouldPreferAnExplicitPlaceOverTheMapView()
    {
        GivenConfirmed("Salalah", "Salalah");

        var result = await _resolver.ResolveAsync(
            ChatId,
            new StudyAreaReference(StudyAreaKind.NamedArea, NamedArea: "Salalah"),
            MapView,
            default);

        result.Area!.DisplayName.Should().Be("Salalah");
    }

    [Fact]
    public async Task ResolveAsync_ShouldUseTheMapViewWhenNothingWasNamed()
    {
        var result = await _resolver.ResolveAsync(ChatId, requested: null, MapView, default);

        result.Area!.DisplayName.Should().Be("the current map view");
        result.Area.Center.Latitude.Should().Be(23.588);
    }

    [Fact]
    public async Task ResolveAsync_ShouldAskWhenAnAmbiguousPlaceIsReturned()
    {
        _locations.ResolveQueryAsync(ChatId, "Springfield", Arg.Any<CancellationToken>())
            .Returns(new LocationResolutionOutcome(
                LocationResolutionOutcomeType.Ambiguous, null, "Which Springfield did you mean?"));

        var result = await _resolver.ResolveAsync(
            ChatId,
            new StudyAreaReference(StudyAreaKind.NamedArea, NamedArea: "Springfield"),
            MapView,
            default);

        result.ClarificationQuestion.Should().Be("Which Springfield did you mean?");
        result.Area.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_ShouldFailWhenThePlaceCannotBeFound()
    {
        _locations.ResolveQueryAsync(ChatId, "Atlantis", Arg.Any<CancellationToken>())
            .Returns(new LocationResolutionOutcome(
                LocationResolutionOutcomeType.NotFound, null, "I couldn't find Atlantis."));

        var result = await _resolver.ResolveAsync(
            ChatId,
            new StudyAreaReference(StudyAreaKind.NamedArea, NamedArea: "Atlantis"),
            MapView,
            default);

        result.FailureReason.Should().Be("I couldn't find Atlantis.");
        result.ClarificationQuestion.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_ShouldAskWhenThereIsNoExplicitPlaceAndNoMapView()
    {
        var result = await _resolver.ResolveAsync(ChatId, requested: null, mapContext: null, default);

        result.ClarificationQuestion.Should().Contain("Which area");
    }

    [Fact]
    public async Task ResolveAsync_ShouldUseAnExplicitGeometry()
    {
        var polygon = new List<GeoPoint>
        {
            new(23.5, 58.3),
            new(23.6, 58.3),
            new(23.6, 58.4),
        };

        var result = await _resolver.ResolveAsync(
            ChatId,
            new StudyAreaReference(StudyAreaKind.ExplicitGeometry, Polygon: polygon),
            MapView,
            default);

        result.Area!.Polygon.Should().HaveCount(3);
    }
}
