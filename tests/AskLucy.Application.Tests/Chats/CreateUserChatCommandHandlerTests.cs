using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Commands.CreateUserChat;
using AskLucy.Domain.Chats;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Chats;

public sealed class CreateUserChatCommandHandlerTests
{
    private readonly IUserChatRepository _repository = Substitute.For<IUserChatRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    [Fact]
    public async Task Handle_ShouldCreateChatOwnedByCurrentUser_AndPersist()
    {
        _currentUser.UserId.Returns("user-1");
        var handler = new CreateUserChatCommandHandler(_repository, _unitOfWork, _currentUser);

        var result = await handler.Handle(new CreateUserChatCommand("My chat", null), CancellationToken.None);

        result.Title.Should().Be("My chat");
        _repository.Received(1).Add(Arg.Is<UserChat>(c => c != null && c.UserId == "user-1" && c.Title == "My chat"));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldThrowUnauthorized_WhenNoCurrentUser()
    {
        _currentUser.UserId.Returns((string?)null);
        var handler = new CreateUserChatCommandHandler(_repository, _unitOfWork, _currentUser);

        var act = () => handler.Handle(new CreateUserChatCommand("My chat", null), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
