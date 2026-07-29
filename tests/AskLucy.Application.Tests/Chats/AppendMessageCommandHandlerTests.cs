using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Commands.AppendMessage;
using AskLucy.Domain.Chats;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Chats;

/// <summary>
/// Covers the 2026-07-28 ChatGPT-style history decision and its ownership scoping (FR-018),
/// plus specs/002-chat-history-management's metadata/attachment/citation persistence
/// (FR-016/FR-017) and first-message auto-titling (FR-013).
/// </summary>
public sealed class AppendMessageCommandHandlerTests
{
    private readonly IUserChatRepository _chatRepository = Substitute.For<IUserChatRepository>();
    private readonly IMessageRepository _messageRepository = Substitute.For<IMessageRepository>();
    private readonly IAIProvider _aiProvider = Substitute.For<IAIProvider>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    public AppendMessageCommandHandlerTests()
    {
        _messageRepository.ListByChatIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Message>());
        _aiProvider.ProviderName.Returns("OpenAI");
        _aiProvider.ChatModel.Returns("gpt-4");
        _aiProvider.ImageModel.Returns("dall-e-3");
    }

    private AppendMessageCommandHandler CreateHandler() =>
        new(_chatRepository, _messageRepository, _aiProvider, _unitOfWork, _currentUser);

    [Fact]
    public async Task Handle_ShouldPersistTheMessage_WhenCallerOwnsTheChat()
    {
        var chat = UserChat.Create("My chat", "owner-1", null, "owner-1");
        _chatRepository.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);
        _currentUser.UserId.Returns("owner-1");
        var handler = CreateHandler();

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
        var handler = CreateHandler();

        var act = () => handler.Handle(
            new AppendMessageCommand(chat.Id, MessageRole.User, MessageKind.Text, "Hijack attempt", null), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        _messageRepository.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task Handle_ShouldAutoPopulateProviderAndModel_ForAssistantMessages()
    {
        var chat = UserChat.Create("My chat", "owner-1", null, "owner-1");
        _chatRepository.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);
        _currentUser.UserId.Returns("owner-1");
        var handler = CreateHandler();

        var result = await handler.Handle(
            new AppendMessageCommand(chat.Id, MessageRole.Assistant, MessageKind.Text, "Hello!", null), CancellationToken.None);

        result.Provider.Should().Be("OpenAI");
        result.Model.Should().Be("gpt-4");
    }

    [Fact]
    public async Task Handle_ShouldUseImageModel_ForImageKindAssistantMessages()
    {
        var chat = UserChat.Create("My chat", "owner-1", null, "owner-1");
        _chatRepository.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);
        _currentUser.UserId.Returns("owner-1");
        var handler = CreateHandler();

        var result = await handler.Handle(
            new AppendMessageCommand(chat.Id, MessageRole.Assistant, MessageKind.Image, "https://example.com/img.png", "a cat"),
            CancellationToken.None);

        result.Model.Should().Be("dall-e-3");
    }

    [Fact]
    public async Task Handle_ShouldPersistAttachmentsAndCitations()
    {
        var chat = UserChat.Create("My chat", "owner-1", null, "owner-1");
        _chatRepository.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);
        _currentUser.UserId.Returns("owner-1");
        var handler = CreateHandler();

        var result = await handler.Handle(
            new AppendMessageCommand(
                chat.Id, MessageRole.Assistant, MessageKind.Text, "Per the handbook...", null,
                Attachments: [new AppendMessageAttachmentInput("policy.pdf", "application/pdf", "/files/policy.pdf")],
                Citations: [new AppendMessageCitationInput("Handbook", "https://example.com/handbook")]),
            CancellationToken.None);

        result.Attachments.Should().ContainSingle(a => a.FileName == "policy.pdf");
        result.Citations.Should().ContainSingle(c => c.SourceLabel == "Handbook");
    }

    [Fact]
    public async Task Handle_ShouldAutoTitleTheChat_OnTheFirstUserMessage_WhenTitleNotManuallySet()
    {
        var chat = UserChat.Create("New chat", "owner-1", null, "owner-1");
        _chatRepository.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);
        _currentUser.UserId.Returns("owner-1");
        var handler = CreateHandler();

        await handler.Handle(
            new AppendMessageCommand(chat.Id, MessageRole.User, MessageKind.Text, "How do I file expenses?", null),
            CancellationToken.None);

        chat.Title.Should().Be("How do I file expenses?");
    }

    [Fact]
    public async Task Handle_ShouldNotAutoTitle_WhenNotTheFirstMessage()
    {
        var chat = UserChat.Create("New chat", "owner-1", null, "owner-1");
        _chatRepository.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);
        _currentUser.UserId.Returns("owner-1");
        _messageRepository.ListByChatIdAsync(chat.Id, Arg.Any<CancellationToken>())
            .Returns([Message.Create(chat.Id, MessageRole.User, MessageKind.Text, "First", null, "owner-1")]);
        var handler = CreateHandler();

        await handler.Handle(
            new AppendMessageCommand(chat.Id, MessageRole.User, MessageKind.Text, "Second message", null),
            CancellationToken.None);

        chat.Title.Should().Be("New chat");
    }
}
