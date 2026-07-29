using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Commands.ClearUserChatMessages;
using AskLucy.Domain.Chats;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Chats;

/// <summary>Covers FR-011 — Clear Messages requires confirmation, removes all messages, and preserves the conversation/title.</summary>
public sealed class ClearUserChatMessagesCommandTests
{
    private readonly IUserChatRepository _chatRepository = Substitute.For<IUserChatRepository>();
    private readonly IMessageRepository _messageRepository = Substitute.For<IMessageRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly ILogger<ClearUserChatMessagesCommandHandler> _logger = Substitute.For<ILogger<ClearUserChatMessagesCommandHandler>>();

    [Fact]
    public void Validator_ShouldReject_WhenConfirmIsFalse()
    {
        var validator = new ClearUserChatMessagesCommandValidator();

        ValidationResult result = validator.Validate(new ClearUserChatMessagesCommand(Guid.NewGuid(), false));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_ShouldAccept_WhenConfirmIsTrue()
    {
        var validator = new ClearUserChatMessagesCommandValidator();

        ValidationResult result = validator.Validate(new ClearUserChatMessagesCommand(Guid.NewGuid(), true));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ShouldDeleteAllMessages_AndPreserveTheConversation()
    {
        var chat = UserChat.Create("My chat", "owner-1", null, "owner-1");
        _chatRepository.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);
        _currentUser.UserId.Returns("owner-1");

        var handler = new ClearUserChatMessagesCommandHandler(_chatRepository, _messageRepository, _currentUser, _logger);

        await handler.Handle(new ClearUserChatMessagesCommand(chat.Id, true), CancellationToken.None);

        await _messageRepository.Received(1).DeleteAllByChatIdAsync(chat.Id, Arg.Any<CancellationToken>());
        chat.Title.Should().Be("My chat", "the conversation and its title survive Clear Messages");
    }

    [Fact]
    public async Task Handle_ShouldThrowNotFound_WhenCallerDoesNotOwnTheChat()
    {
        var chat = UserChat.Create("Someone else's chat", "owner-1", null, "owner-1");
        _chatRepository.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);
        _currentUser.UserId.Returns("attacker-2");
        var handler = new ClearUserChatMessagesCommandHandler(_chatRepository, _messageRepository, _currentUser, _logger);

        var act = () => handler.Handle(new ClearUserChatMessagesCommand(chat.Id, true), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        await _messageRepository.DidNotReceiveWithAnyArgs().DeleteAllByChatIdAsync(default, default);
    }
}
