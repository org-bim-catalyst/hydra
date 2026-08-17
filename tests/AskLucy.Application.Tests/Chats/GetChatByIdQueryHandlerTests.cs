using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Queries.GetChatById;
using AskLucy.Domain.Chats;
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
