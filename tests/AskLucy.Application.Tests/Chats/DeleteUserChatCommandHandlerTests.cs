using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Commands.DeleteUserChat;
using AskLucy.Domain.Chats;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Chats;

public sealed class DeleteUserChatCommandHandlerTests
{
    private readonly IUserChatRepository _repository = Substitute.For<IUserChatRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    [Fact]
    public async Task Handle_ShouldSoftDelete_WhenCallerOwnsTheChat()
    {
        var chat = UserChat.Create("My chat", "owner-1", null, "owner-1");
        _repository.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);
        _currentUser.UserId.Returns("owner-1");
        var handler = new DeleteUserChatCommandHandler(_repository, _unitOfWork, _currentUser);

        await handler.Handle(new DeleteUserChatCommand(chat.Id), CancellationToken.None);

        chat.IsDeleted.Should().BeTrue();
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldThrowNotFound_WhenCallerDoesNotOwnTheChat()
    {
        var chat = UserChat.Create("Someone else's chat", "owner-1", null, "owner-1");
        _repository.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);
        _currentUser.UserId.Returns("attacker-2");
        var handler = new DeleteUserChatCommandHandler(_repository, _unitOfWork, _currentUser);

        var act = () => handler.Handle(new DeleteUserChatCommand(chat.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        chat.IsDeleted.Should().BeFalse();
    }
}
