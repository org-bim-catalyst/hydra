using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Queries.GetChatMessages;
using AskLucy.Domain.Chats;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Chats;

public sealed class GetChatMessagesQueryHandlerTests
{
    private readonly IUserChatRepository _chatRepository = Substitute.For<IUserChatRepository>();
    private readonly IMessageRepository _messageRepository = Substitute.For<IMessageRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    [Fact]
    public async Task Handle_ShouldReturnMessagesInOrder_WhenCallerOwnsTheChat()
    {
        var chat = UserChat.Create("My chat", "owner-1", null, "owner-1");
        _chatRepository.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);
        _currentUser.UserId.Returns("owner-1");

        var first = Message.Create(chat.Id, MessageRole.User, MessageKind.Text, "Hi", null, "owner-1");
        var second = Message.Create(chat.Id, MessageRole.Assistant, MessageKind.Text, "Hello!", null, "owner-1");
        _messageRepository.ListPagedByChatIdAsync(chat.Id, null, 50, Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<Message>)[first, second], (string?)null));

        var handler = new GetChatMessagesQueryHandler(_chatRepository, _messageRepository, _currentUser);

        var result = await handler.Handle(new GetChatMessagesQuery(chat.Id), CancellationToken.None);

        result.Items.Should().HaveCount(2);
        result.Items[0].Content.Should().Be("Hi");
        result.Items[1].Content.Should().Be("Hello!");
        result.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ShouldReturnNextCursor_WhenMorePagesRemain()
    {
        var chat = UserChat.Create("My chat", "owner-1", null, "owner-1");
        _chatRepository.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);
        _currentUser.UserId.Returns("owner-1");

        var message = Message.Create(chat.Id, MessageRole.User, MessageKind.Text, "Hi", null, "owner-1");
        _messageRepository.ListPagedByChatIdAsync(chat.Id, null, 1, Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<Message>)[message], "next-cursor"));

        var handler = new GetChatMessagesQueryHandler(_chatRepository, _messageRepository, _currentUser);

        var result = await handler.Handle(new GetChatMessagesQuery(chat.Id, PageSize: 1), CancellationToken.None);

        result.NextCursor.Should().Be("next-cursor");
    }

    [Fact]
    public async Task Handle_ShouldThrowNotFound_WhenCallerDoesNotOwnTheChat()
    {
        var chat = UserChat.Create("Someone else's chat", "owner-1", null, "owner-1");
        _chatRepository.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);
        _currentUser.UserId.Returns("attacker-2");
        var handler = new GetChatMessagesQueryHandler(_chatRepository, _messageRepository, _currentUser);

        var act = () => handler.Handle(new GetChatMessagesQuery(chat.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
