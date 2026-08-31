using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Commands.DuplicateUserChat;
using AskLucy.Domain.Chats;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Chats;

/// <summary>Covers FR-010 (full-copy duplicate) and the duplicate-starts-plain edge case (spec.md Edge Cases).</summary>
public sealed class DuplicateUserChatCommandTests
{
    private readonly IUserChatRepository _chatRepository = Substitute.For<IUserChatRepository>();
    private readonly IMessageRepository _messageRepository = Substitute.For<IMessageRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    [Fact]
    public async Task Handle_ShouldCreateIndependentCopy_WithAllMessages_LeavingSourceUnchanged()
    {
        var source = UserChat.Create("Source chat", "owner-1", null, "owner-1");
        source.Pin("owner-1");
        source.MarkFavorite("owner-1");
        source.Archive("owner-1");
        _chatRepository.GetByIdAsync(source.Id, Arg.Any<CancellationToken>()).Returns(source);
        _currentUser.UserId.Returns("owner-1");

        var message1 = Message.Create(source.Id, MessageRole.User, MessageKind.Text, "Hi", null, "owner-1");
        var message2 = Message.Create(source.Id, MessageRole.Assistant, MessageKind.Text, "Hello!", null, "owner-1", "OpenAI", "gpt-4");
        _messageRepository.ListByChatIdAsync(source.Id, Arg.Any<CancellationToken>()).Returns([message1, message2]);

        var handler = new DuplicateUserChatCommandHandler(_chatRepository, _messageRepository, _unitOfWork, _currentUser);

        var result = await handler.Handle(new DuplicateUserChatCommand(source.Id), CancellationToken.None);

        result.Id.Should().NotBe(source.Id);
        result.Title.Should().Be("Source chat");
        // Edge case: the duplicate starts plain regardless of the source's archive/pin/favorite state.
        result.IsPinned.Should().BeFalse();
        result.IsFavorite.Should().BeFalse();
        result.IsArchived.Should().BeFalse();

        _messageRepository.Received(2).Add(Arg.Is<Message>(m => m != null && m.UserChatId == result.Id));
        _chatRepository.Received(1).Add(Arg.Is<UserChat>(c => c != null && c.Id == result.Id));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        // The source itself must never be mutated by duplication.
        source.PinnedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_ShouldThrowNotFound_WhenCallerDoesNotOwnTheSourceChat()
    {
        var source = UserChat.Create("Source chat", "owner-1", null, "owner-1");
        _chatRepository.GetByIdAsync(source.Id, Arg.Any<CancellationToken>()).Returns(source);
        _currentUser.UserId.Returns("attacker-2");
        var handler = new DuplicateUserChatCommandHandler(_chatRepository, _messageRepository, _unitOfWork, _currentUser);

        var act = () => handler.Handle(new DuplicateUserChatCommand(source.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
