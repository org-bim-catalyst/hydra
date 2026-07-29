using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Commands.RenameUserChat;
using AskLucy.Domain.Chats;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Chats;

/// <summary>
/// Covers FR-033 (rename) and the ownership scoping (FR-018, User Story 3) that
/// RenameUserChatCommandHandler enforces directly.
/// </summary>
public sealed class RenameUserChatCommandHandlerTests
{
    private readonly IUserChatRepository _repository = Substitute.For<IUserChatRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    [Fact]
    public async Task Handle_ShouldRename_WhenCallerOwnsTheChat()
    {
        var chat = UserChat.Create("Old title", "owner-1", null, "owner-1");
        _repository.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);
        _currentUser.UserId.Returns("owner-1");
        var handler = new RenameUserChatCommandHandler(_repository, _unitOfWork, _currentUser);

        var result = await handler.Handle(new RenameUserChatCommand(chat.Id, "New title"), CancellationToken.None);

        result.Title.Should().Be("New title");
        chat.IsTitleManuallySet.Should().BeTrue("FR-014: a manual rename must freeze auto-title generation");
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldThrowNotFound_WhenCallerDoesNotOwnTheChat()
    {
        var chat = UserChat.Create("Someone else's chat", "owner-1", null, "owner-1");
        _repository.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);
        _currentUser.UserId.Returns("attacker-2");
        var handler = new RenameUserChatCommandHandler(_repository, _unitOfWork, _currentUser);

        var act = () => handler.Handle(new RenameUserChatCommand(chat.Id, "Hijacked"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Handle_ShouldThrowNotFound_WhenChatDoesNotExist()
    {
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((UserChat?)null);
        _currentUser.UserId.Returns("owner-1");
        var handler = new RenameUserChatCommandHandler(_repository, _unitOfWork, _currentUser);

        var act = () => handler.Handle(new RenameUserChatCommand(Guid.NewGuid(), "New title"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
