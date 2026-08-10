using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Chats;
using AskLucy.Application.Chats.Commands.AppendMessage;
using AskLucy.Application.Prompts.Commands.InsertPromptIntoConversation;
using AskLucy.Application.Prompts.Commands.RecordPromptExecution;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Chats;
using AskLucy.Domain.Prompts;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Prompts;

/// <summary>
/// tasks.md T097. On a successful send, a <see cref="RecordPromptExecutionCommand"/>
/// (<c>Origin: ConversationInsertion</c>, <c>ResultMessageId</c> set) must be recorded — the sole
/// path that later increments <see cref="PromptUsageStatistics"/> (see
/// <c>RecentlyUsedOrderingTests</c> for that handler's own coverage). On a mid-stream provider
/// failure, neither the assistant message nor the execution row is ever recorded (spec.md
/// FR-051, User Story 5 AC2) — the handler has no try/catch around the delegated stream by design
/// (an iterator cannot wrap a `yield` in try/catch, same constraint as
/// <c>ExecutePromptCommandHandler</c>).
/// </summary>
public sealed class InsertPromptUsageTrackingTests
{
    private const string OwnerId = "user-1";

    private readonly IPromptRepository _promptRepository = Substitute.For<IPromptRepository>();
    private readonly IUserChatRepository _userChatRepository = Substitute.For<IUserChatRepository>();
    private readonly IMessageRepository _messageRepository = Substitute.For<IMessageRepository>();
    private readonly IAIProviderRepository _providerRepository = Substitute.For<IAIProviderRepository>();
    private readonly IAIModelRepository _modelRepository = Substitute.For<IAIModelRepository>();
    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private InsertPromptIntoConversationCommandHandler CreateHandler() => new(
        _promptRepository, _userChatRepository, _messageRepository, _providerRepository, _modelRepository, _mediator, _currentUser);

    private static (Prompt Prompt, PromptVersion Version) BuildPrompt()
    {
        var content = new PromptContentSnapshot(
            null, null, "Summarize {{document}}.", null, null, null, null, null, null, null, null, false);
        var variables = new List<PromptVariableDefinition>
        {
            new("document", null, PromptVariableType.String, true, null, null, null, 0),
        };
        return Prompt.Create(
            OwnerId, $"Prompt {Guid.NewGuid():N}", null, PromptType.Chat, null, null,
            PromptCapabilityRequirements.None, null, content, variables, OwnerId);
    }

    private (UserChat Chat, AIProvider Provider, AIModel Model) SetUpChat()
    {
        var provider = AIProvider.Create("openai", "OpenAI", "system");
        var model = AIModel.Create(
            provider.Id, "gpt-5", "GPT-5", 128000, 4096,
            new AIModelCapabilities(true, false, false, false, false, false, false, false, false), null, null, "system");
        var chat = UserChat.Create("Conversation", OwnerId, null, OwnerId);
        chat.SetModelSelection(provider.Id, model.Id, null, OwnerId);

        _providerRepository.GetByIdAsync(provider.Id, Arg.Any<CancellationToken>()).Returns(provider);
        _modelRepository.GetByIdAsync(model.Id, Arg.Any<CancellationToken>()).Returns(model);
        _userChatRepository.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);

        return (chat, provider, model);
    }

    private static MessageDto BuildMessageDto(MessageRole role) => new(
        Guid.NewGuid(), role.ToString(), MessageKind.Text.ToString(), "content", null, DateTime.UtcNow,
        null, null, null, null, null, null, null, null, null, [], []);

    private static async IAsyncEnumerable<ChatStreamChunk> StreamOf(params ChatStreamChunk[] chunks)
    {
        foreach (var chunk in chunks)
        {
            await Task.Yield();
            yield return chunk;
        }
    }

    [Fact]
    public async Task Handle_ShouldRecordPromptExecution_WithConversationInsertionOriginAndResultMessageId_OnSuccess()
    {
        var (prompt, version) = BuildPrompt();
        var (chat, _, _) = SetUpChat();
        _currentUser.UserId.Returns(OwnerId);
        _promptRepository.GetByIdForOwnerAsync(prompt.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(prompt);
        _promptRepository.GetVersionAsync(prompt.Id, prompt.CurrentVersionNumber, Arg.Any<CancellationToken>()).Returns(version);
        _messageRepository.ListByChatIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(new List<Message>());

        var assistantMessage = BuildMessageDto(MessageRole.Assistant);
        _mediator.Send(Arg.Is<AppendMessageCommand>(c => c.Role == MessageRole.User), Arg.Any<CancellationToken>())
            .Returns(BuildMessageDto(MessageRole.User));
        _mediator.Send(Arg.Is<AppendMessageCommand>(c => c.Role == MessageRole.Assistant), Arg.Any<CancellationToken>())
            .Returns(assistantMessage);
        _mediator.CreateStream(Arg.Any<SendChatMessageCommand>(), Arg.Any<CancellationToken>())
            .Returns(StreamOf(new ChatStreamChunk("A summary.", new ChatUsage(10, 5, null, null, 100))));

        var command = new InsertPromptIntoConversationCommand(chat.Id, prompt.Id, new Dictionary<string, string?> { ["document"] = "my report" });

        await foreach (var _ in CreateHandler().Handle(command, CancellationToken.None))
        {
        }

        await _mediator.Received(1).Send(
            Arg.Is<RecordPromptExecutionCommand>(c =>
                c.Origin == PromptExecutionOrigin.ConversationInsertion &&
                c.Outcome == PromptExecutionOutcome.Success &&
                c.ResultMessageId == assistantMessage.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldRecordNeitherTheAssistantMessageNorTheExecution_WhenTheProviderFailsMidStream()
    {
        var (prompt, version) = BuildPrompt();
        var (chat, _, _) = SetUpChat();
        _currentUser.UserId.Returns(OwnerId);
        _promptRepository.GetByIdForOwnerAsync(prompt.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(prompt);
        _promptRepository.GetVersionAsync(prompt.Id, prompt.CurrentVersionNumber, Arg.Any<CancellationToken>()).Returns(version);
        _messageRepository.ListByChatIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(new List<Message>());

        _mediator.Send(Arg.Is<AppendMessageCommand>(c => c.Role == MessageRole.User), Arg.Any<CancellationToken>())
            .Returns(BuildMessageDto(MessageRole.User));
        _mediator.CreateStream(Arg.Any<SendChatMessageCommand>(), Arg.Any<CancellationToken>())
            .Returns(ThrowingStream());

        var command = new InsertPromptIntoConversationCommand(chat.Id, prompt.Id, new Dictionary<string, string?> { ["document"] = "my report" });

        var act = async () =>
        {
            await foreach (var _ in CreateHandler().Handle(command, CancellationToken.None))
            {
            }
        };

        await act.Should().ThrowAsync<AiProviderUnavailableException>();
        await _mediator.DidNotReceive().Send(Arg.Is<AppendMessageCommand>(c => c.Role == MessageRole.Assistant), Arg.Any<CancellationToken>());
        await _mediator.DidNotReceive().Send(Arg.Any<RecordPromptExecutionCommand>(), Arg.Any<CancellationToken>());
    }

    private static async IAsyncEnumerable<ChatStreamChunk> ThrowingStream()
    {
        await Task.Yield();
        throw new AiProviderUnavailableException("Provider is down.");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }
}
