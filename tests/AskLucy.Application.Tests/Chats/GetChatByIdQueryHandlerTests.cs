using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Queries.GetChatById;
using AskLucy.Domain.Chats;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Chats;

/// <summary>
/// specs/025-chat-configuration-settings — covers the one gap identified in research.md
/// Decision 2: <see cref="UserChat.ProviderId"/>/<see cref="UserChat.ModelId"/> were already
/// persisted but never queryable. Ownership scoping (FR-018) mirrors
/// RenameUserChatCommandHandlerTests's existing convention.
/// </summary>
public sealed class GetChatByIdQueryHandlerTests
{
    private readonly IUserChatRepository _repository = Substitute.For<IUserChatRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    [Fact]
    public async Task Handle_ShouldReturnChatDetail_WhenCallerOwnsTheChat()
    {
        var chat = UserChat.Create("Steel connection tolerances", "owner-1", null, "owner-1");
        var providerId = Guid.NewGuid();
        var modelId = Guid.NewGuid();
        chat.SetModelSelection(providerId, modelId, generationParametersJson: null, "owner-1");
        _repository.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);
        _currentUser.UserId.Returns("owner-1");
        var handler = new GetChatByIdQueryHandler(_repository, _currentUser);

        var result = await handler.Handle(new GetChatByIdQuery(chat.Id), CancellationToken.None);

        result.Id.Should().Be(chat.Id);
        result.Title.Should().Be("Steel connection tolerances");
        result.ProviderId.Should().Be(providerId);
        result.ModelId.Should().Be(modelId);
    }

    [Fact]
    public async Task Handle_ShouldReturnNullProviderAndModel_WhenChatHasNoSelectionYet()
    {
        var chat = UserChat.Create("Brand-new chat", "owner-1", null, "owner-1");
        _repository.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);
        _currentUser.UserId.Returns("owner-1");
        var handler = new GetChatByIdQueryHandler(_repository, _currentUser);

        var result = await handler.Handle(new GetChatByIdQuery(chat.Id), CancellationToken.None);

        result.ProviderId.Should().BeNull();
        result.ModelId.Should().BeNull();
    }

    // A reload (or a turn cut short) used to leave the viewer without the site the conversation
    // still considered current: the persisted location/boundary only ever reached the client
    // through a live reply's stream.
    [Fact]
    public async Task Handle_ShouldReturnTheActiveSite_WhenTheChatHasConfirmedOne()
    {
        var chat = UserChat.Create("Park survey", "owner-1", null, "owner-1");
        chat.SetActiveLocation(25.1558, 55.2218, "Al Safa Park 2", 0.9, "owner-1", "ROOFTOP");
        chat.SetActiveBoundary(
            "Al Safa Park 2", 25.1560, 55.2220,
            [new GeoPoint(25.15, 55.22), new GeoPoint(25.16, 55.22), new GeoPoint(25.16, 55.23), new GeoPoint(25.15, 55.22)],
            15146.14, 0.92, BoundaryConfidenceLevel.High, SiteBoundarySource.OsmBoundary,
            "OpenStreetMap (leisure=park)", "owner-1");
        _repository.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);
        _currentUser.UserId.Returns("owner-1");
        var handler = new GetChatByIdQueryHandler(_repository, _currentUser);

        var result = await handler.Handle(new GetChatByIdQuery(chat.Id), CancellationToken.None);

        result.ActiveLocation.Should().Be(new ChatActiveLocationDto(25.1558, 55.2218, "Al Safa Park 2", 0.9, "high"));
        result.ActiveBoundary.Should().NotBeNull();
        result.ActiveBoundary!.SiteName.Should().Be("Al Safa Park 2");
        result.ActiveBoundary.Centroid.Should().Be(new ChatGeoPointDto(25.1560, 55.2220));
        result.ActiveBoundary.Polygon.Should().HaveCount(4);
        result.ActiveBoundary.Polygon[1].Should().Be(new ChatGeoPointDto(25.16, 55.22));
        result.ActiveBoundary.AreaSquareMeters.Should().Be(15146.14);
        result.ActiveBoundary.Confidence.Should().Be(0.92);
        result.ActiveBoundary.ConfidenceLevel.Should().Be("high", "the client reads the same lower-case level the live event carries");
        result.ActiveBoundary.Source.Should().Be("OsmBoundary");
        result.ActiveBoundary.SourceDetail.Should().Be("OpenStreetMap (leisure=park)");
    }

    [Fact]
    public async Task Handle_ShouldReturnNoActiveSite_WhenTheChatHasNotConfirmedOne()
    {
        var chat = UserChat.Create("No site yet", "owner-1", null, "owner-1");
        _repository.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);
        _currentUser.UserId.Returns("owner-1");
        var handler = new GetChatByIdQueryHandler(_repository, _currentUser);

        var result = await handler.Handle(new GetChatByIdQuery(chat.Id), CancellationToken.None);

        result.ActiveLocation.Should().BeNull();
        result.ActiveBoundary.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ShouldThrowNotFound_WhenCallerDoesNotOwnTheChat()
    {
        var chat = UserChat.Create("Someone else's chat", "owner-1", null, "owner-1");
        _repository.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);
        _currentUser.UserId.Returns("attacker-2");
        var handler = new GetChatByIdQueryHandler(_repository, _currentUser);

        var act = () => handler.Handle(new GetChatByIdQuery(chat.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Handle_ShouldThrowNotFound_WhenChatDoesNotExist()
    {
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((UserChat?)null);
        _currentUser.UserId.Returns("owner-1");
        var handler = new GetChatByIdQueryHandler(_repository, _currentUser);

        var act = () => handler.Handle(new GetChatByIdQuery(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
