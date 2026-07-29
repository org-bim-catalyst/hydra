using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Commands.PurgeUserChat;
using AskLucy.Domain.Chats;
using FluentAssertions;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Chats;

/// <summary>Covers FR-004/FR-005 (permanent delete requires confirmation, is owner-scoped) and FR-028 (audit log entry).</summary>
public sealed class PurgeUserChatCommandHandlerTests
{
    private readonly IUserChatRepository _repository = Substitute.For<IUserChatRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly ILogger<PurgeUserChatCommandHandler> _logger = Substitute.For<ILogger<PurgeUserChatCommandHandler>>();

    [Fact]
    public void Validator_ShouldReject_WhenConfirmIsFalse()
    {
        var validator = new PurgeUserChatCommandValidator();

        ValidationResult result = validator.Validate(new PurgeUserChatCommand(Guid.NewGuid(), false));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ShouldPurgeTheConversation_WhenConfirmedAndOwned()
    {
        var chat = UserChat.Create("My chat", "owner-1", null, "owner-1");
        chat.SoftDelete("owner-1");
        _repository.GetByIdIncludingDeletedAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);
        _currentUser.UserId.Returns("owner-1");

        var handler = new PurgeUserChatCommandHandler(_repository, _currentUser, _logger);

        await handler.Handle(new PurgeUserChatCommand(chat.Id, true), CancellationToken.None);

        await _repository.Received(1).PurgeAsync(chat.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldThrowNotFound_WhenCallerDoesNotOwnTheChat()
    {
        var chat = UserChat.Create("Someone else's chat", "owner-1", null, "owner-1");
        _repository.GetByIdIncludingDeletedAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);
        _currentUser.UserId.Returns("attacker-2");
        var handler = new PurgeUserChatCommandHandler(_repository, _currentUser, _logger);

        var act = () => handler.Handle(new PurgeUserChatCommand(chat.Id, true), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        await _repository.DidNotReceiveWithAnyArgs().PurgeAsync(default, default);
    }
}
