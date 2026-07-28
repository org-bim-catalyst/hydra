using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Commands.AppendMessage;
using AskLucy.Domain.Chats;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Chats;

/// <summary>Covers the 2026-07-28 ChatGPT-style history decision and its ownership scoping (FR-018).</summary>
public sealed class AppendMessageCommandHandlerTests
{
    private readonly IUserChatRepository _chatRepository = Substitute.For<IUserChatRepository>();
    private readonly IMessageRepository _messageRepository = Substitute.For<IMessageRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    [Fact]
    public async Task Handle_ShouldPersistTheMessage_WhenCallerOwnsTheChat()
    {
        var chat = UserChat.Create("My chat", "owner-1", null, "owner-1");
        _chatRepository.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);
        _currentUser.UserId.Returns("owner-1");
        var handler = new AppendMessageCommandHandler(_chatRepository, _messageRepository, _unitOfWork, _currentUser);

        var result = await handler.Handle(
            new AppendMessageCommand(chat.Id, MessageRole.User, MessageKind.Text, "Hello", null), CancellationToken.None);

        result.Content.Should().Be("Hello");
        result.Role.Should().Be(nameof(MessageRole.User));
        _messageRepository.Received(1).Add(Arg.Any<Message>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldThrowNotFound_WhenCallerDoesNotOwnTheChat()
    {
        var chat = UserChat.Create("Someone else's chat", "owner-1", null, "owner-1");
        _chatRepository.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);
        _currentUser.UserId.Returns("attacker-2");
        var handler = new AppendMessageCommandHandler(_chatRepository, _messageRepository, _unitOfWork, _currentUser);

        var act = () => handler.Handle(
            new AppendMessageCommand(chat.Id, MessageRole.User, MessageKind.Text, "Hijack attempt", null), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        _messageRepository.DidNotReceiveWithAnyArgs().Add(default!);
    }
}
