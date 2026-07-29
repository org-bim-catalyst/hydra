using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Queries.ExportUserChat;
using AskLucy.Domain.Chats;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Chats;

/// <summary>Covers FR-025 — export shape, attachment/citation references (not embedded content), and the zero-message edge case.</summary>
public sealed class ExportUserChatQueryTests
{
    private readonly IUserChatRepository _chatRepository = Substitute.For<IUserChatRepository>();
    private readonly IMessageRepository _messageRepository = Substitute.For<IMessageRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    [Fact]
    public async Task Handle_ShouldIncludeFullOrderedHistory_WithAttachmentAndCitationReferences()
    {
        var chat = UserChat.Create("My chat", "owner-1", null, "owner-1");
        _chatRepository.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);
        _currentUser.UserId.Returns("owner-1");

        var message = Message.Create(chat.Id, MessageRole.Assistant, MessageKind.Text, "Per the handbook...", null, "owner-1", "OpenAI", "gpt-4");
        message.AddAttachment("policy.pdf", "application/pdf", "/files/policy.pdf", "owner-1");
        message.AddCitation("Handbook", "https://example.com/handbook", "owner-1");
        _messageRepository.ListByChatIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns([message]);

        var handler = new ExportUserChatQueryHandler(_chatRepository, _messageRepository, _currentUser);

        var result = await handler.Handle(new ExportUserChatQuery(chat.Id), CancellationToken.None);

        result.Title.Should().Be("My chat");
        result.Messages.Should().ContainSingle();
        var exportedMessage = result.Messages[0];
        exportedMessage.Attachments.Should().ContainSingle(a => a.FileName == "policy.pdf" && a.AccessLocation == "/files/policy.pdf");
        exportedMessage.Citations.Should().ContainSingle(c => c.SourceLabel == "Handbook");
    }

    [Fact]
    public async Task Handle_ShouldReturnEmptyMessagesArray_WhenConversationHasNoMessages()
    {
        var chat = UserChat.Create("Empty chat", "owner-1", null, "owner-1");
        _chatRepository.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);
        _currentUser.UserId.Returns("owner-1");
        _messageRepository.ListByChatIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns([]);

        var handler = new ExportUserChatQueryHandler(_chatRepository, _messageRepository, _currentUser);

        var result = await handler.Handle(new ExportUserChatQuery(chat.Id), CancellationToken.None);

        result.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ShouldThrowNotFound_WhenCallerDoesNotOwnTheChat()
    {
        var chat = UserChat.Create("Someone else's chat", "owner-1", null, "owner-1");
        _chatRepository.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);
        _currentUser.UserId.Returns("attacker-2");
        var handler = new ExportUserChatQueryHandler(_chatRepository, _messageRepository, _currentUser);

        var act = () => handler.Handle(new ExportUserChatQuery(chat.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
