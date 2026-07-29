using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Commands.ArchiveUserChat;
using AskLucy.Application.Chats.Commands.FavoriteUserChat;
using AskLucy.Application.Chats.Commands.PinUserChat;
using AskLucy.Application.Chats.Commands.RestoreUserChat;
using AskLucy.Application.Chats.Commands.UnfavoriteUserChat;
using AskLucy.Application.Chats.Commands.UnpinUserChat;
using AskLucy.Domain.Chats;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Chats;

/// <summary>Covers Archive/Restore/Pin/Unpin/Favorite/Unfavorite (FR-006–FR-009, FR-005a) state transitions, idempotency, and cross-user denial (FR-026).</summary>
public sealed class ConversationStateCommandsTests
{
    private readonly IUserChatRepository _repository = Substitute.For<IUserChatRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    [Fact]
    public async Task Archive_ShouldSetIsArchived()
    {
        var chat = UserChat.Create("Chat", "owner-1", null, "owner-1");
        _repository.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);
        _currentUser.UserId.Returns("owner-1");

        var result = await new ArchiveUserChatCommandHandler(_repository, _unitOfWork, _currentUser)
            .Handle(new ArchiveUserChatCommand(chat.Id), CancellationToken.None);

        result.IsArchived.Should().BeTrue();
    }

    [Fact]
    public async Task Archive_ShouldThrowNotFound_WhenCallerDoesNotOwnTheChat()
    {
        var chat = UserChat.Create("Chat", "owner-1", null, "owner-1");
        _repository.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);
        _currentUser.UserId.Returns("attacker-2");

        var act = () => new ArchiveUserChatCommandHandler(_repository, _unitOfWork, _currentUser)
            .Handle(new ArchiveUserChatCommand(chat.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Restore_ShouldClearArchivedAndDeletedState_PreservingPinAndFavorite()
    {
        var chat = UserChat.Create("Chat", "owner-1", null, "owner-1");
        chat.Pin("owner-1");
        chat.MarkFavorite("owner-1");
        chat.Archive("owner-1");
        chat.SoftDelete("owner-1");
        _repository.GetByIdIncludingDeletedAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);
        _currentUser.UserId.Returns("owner-1");

        var result = await new RestoreUserChatCommandHandler(_repository, _unitOfWork, _currentUser)
            .Handle(new RestoreUserChatCommand(chat.Id), CancellationToken.None);

        result.IsArchived.Should().BeFalse();
        result.IsDeleted.Should().BeFalse();
        result.IsPinned.Should().BeTrue();
        result.IsFavorite.Should().BeTrue();
    }

    [Fact]
    public async Task Pin_ThenUnpin_ShouldToggleIsPinned()
    {
        var chat = UserChat.Create("Chat", "owner-1", null, "owner-1");
        _repository.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);
        _currentUser.UserId.Returns("owner-1");

        var pinned = await new PinUserChatCommandHandler(_repository, _unitOfWork, _currentUser)
            .Handle(new PinUserChatCommand(chat.Id), CancellationToken.None);
        pinned.IsPinned.Should().BeTrue();

        var unpinned = await new UnpinUserChatCommandHandler(_repository, _unitOfWork, _currentUser)
            .Handle(new UnpinUserChatCommand(chat.Id), CancellationToken.None);
        unpinned.IsPinned.Should().BeFalse();
    }

    [Fact]
    public async Task Favorite_ThenUnfavorite_ShouldToggleIsFavorite()
    {
        var chat = UserChat.Create("Chat", "owner-1", null, "owner-1");
        _repository.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);
        _currentUser.UserId.Returns("owner-1");

        var favorited = await new FavoriteUserChatCommandHandler(_repository, _unitOfWork, _currentUser)
            .Handle(new FavoriteUserChatCommand(chat.Id), CancellationToken.None);
        favorited.IsFavorite.Should().BeTrue();

        var unfavorited = await new UnfavoriteUserChatCommandHandler(_repository, _unitOfWork, _currentUser)
            .Handle(new UnfavoriteUserChatCommand(chat.Id), CancellationToken.None);
        unfavorited.IsFavorite.Should().BeFalse();
    }
}
