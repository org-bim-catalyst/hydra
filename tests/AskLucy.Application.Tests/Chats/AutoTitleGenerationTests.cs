using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Commands.AppendMessage;
using AskLucy.Application.Chats.Commands.RenameUserChat;
using AskLucy.Domain.Chats;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Chats;

/// <summary>
/// End-to-end (across two handlers) coverage of FR-013/FR-014: a manual rename permanently
/// freezes auto-title generation, even for messages sent afterward. Per-handler coverage
/// lives in <c>AppendMessageCommandHandlerTests</c> and <c>RenameUserChatCommandHandlerTests</c>.
/// </summary>
public sealed class AutoTitleGenerationTests
{
    private readonly IUserChatRepository _chatRepository = Substitute.For<IUserChatRepository>();
    private readonly IMessageRepository _messageRepository = Substitute.For<IMessageRepository>();
    private readonly IAIProvider _aiProvider = Substitute.For<IAIProvider>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    [Fact]
    public async Task ManualRename_ShouldPermanentlyFreezeAutoTitling_EvenForLaterMessages()
    {
        var chat = UserChat.Create("New chat", "owner-1", null, "owner-1");
        _chatRepository.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);
        _currentUser.UserId.Returns("owner-1");
        _messageRepository.ListByChatIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns([]);

        var renameHandler = new RenameUserChatCommandHandler(_chatRepository, _unitOfWork, _currentUser);
        await renameHandler.Handle(new RenameUserChatCommand(chat.Id, "My custom title"), CancellationToken.None);
        chat.Title.Should().Be("My custom title");

        var appendHandler = new AppendMessageCommandHandler(_chatRepository, _messageRepository, _aiProvider, _unitOfWork, _currentUser);
        await appendHandler.Handle(
            new AppendMessageCommand(chat.Id, MessageRole.User, MessageKind.Text, "This is the first message ever sent", null),
            CancellationToken.None);

        chat.Title.Should().Be("My custom title", "FR-014: auto-title generation must never overwrite a manually-set title");
    }
}
