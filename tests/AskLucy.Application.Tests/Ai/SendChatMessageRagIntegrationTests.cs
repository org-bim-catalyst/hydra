using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Retrieval;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Ai;

/// <summary>
/// tasks.md T047 (US1 AC1–AC3) — <see cref="SendChatMessageCommandHandler"/> augments the prompt
/// and attaches the retrieval outcome/citations to the final stream chunk when the conversation
/// has an attached knowledge base with relevant content; a conversation with none attached is
/// completely unaffected (no retrieval attempt at all).
/// </summary>
public sealed class SendChatMessageRagIntegrationTests
{
    private readonly IAIProvider _resolvedProvider = Substitute.For<IAIProvider>();
    private readonly IAIProviderResolver _resolver = Substitute.For<IAIProviderResolver>();
    private readonly IAIProviderRepository _providers = Substitute.For<IAIProviderRepository>();
    private readonly IAIModelRepository _models = Substitute.For<IAIModelRepository>();
    private readonly IConversationKnowledgeBaseRepository _conversationKnowledgeBases = Substitute.For<IConversationKnowledgeBaseRepository>();
    private readonly IRagService _ragService = Substitute.For<IRagService>();
    private readonly SendChatMessageCommandHandler _handler;
    private readonly AIProvider _openAiProvider;
    private readonly AIModel _gpt41;
    private readonly Guid _chatId = Guid.NewGuid();
    private readonly Guid _knowledgeBaseId = Guid.NewGuid();

    public SendChatMessageRagIntegrationTests()
    {
        _openAiProvider = AIProvider.Create("openai", "OpenAI", "test");
        _openAiProvider.SetCredential("ciphertext", "test");
        _openAiProvider.Enable("test");

        _gpt41 = AIModel.Create(
            _openAiProvider.Id, "gpt-4.1", "GPT-4.1", 128000, 16384,
            new AIModelCapabilities(true, true, true, true, false, false, true, false, false), null, null, "test");

        _providers.GetByIdAsync(_openAiProvider.Id, Arg.Any<CancellationToken>()).Returns(_openAiProvider);
        _models.GetByIdAsync(_gpt41.Id, Arg.Any<CancellationToken>()).Returns(_gpt41);
        _resolver.Resolve("openai").Returns(_resolvedProvider);
        _resolvedProvider
            .StreamChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), "gpt-4.1", Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable([new StreamChunk("Answer.")]));

        _handler = new SendChatMessageCommandHandler(
            _resolver, _providers, _models, _conversationKnowledgeBases, _ragService, new SendChatMessageCommandValidator(_providers, _models));
    }

    [Fact]
    public async Task Handle_ShouldAugmentPromptAndAttachCitations_WhenKnowledgeBaseHasRelevantContent()
    {
        _conversationKnowledgeBases.GetByConversationAsync(_chatId, Arg.Any<CancellationToken>())
            .Returns(new List<ConversationKnowledgeBase> { ConversationKnowledgeBase.Create(_chatId, _knowledgeBaseId, "test") });

        var citation = new RagCitationContext(
            Guid.NewGuid(), _knowledgeBaseId, Guid.NewGuid(), Guid.NewGuid(), "Doc.pdf", "KB", 3, "Intro", "Relevant excerpt.");
        var outcome = new RagRetrievalOutcome(RagRetrievalOutcomeType.Grounded, "Relevant excerpt.", [citation], null);
        _ragService.RetrieveContextAsync(_chatId, "What's in the docs?", Arg.Is<IReadOnlyList<Guid>>(ids => ids.Contains(_knowledgeBaseId)), Arg.Any<CancellationToken>())
            .Returns(outcome);

        var command = new SendChatMessageCommand(_chatId, [new ChatMessageDto("user", "What's in the docs?")], _openAiProvider.Id, _gpt41.Id, null);

        var chunks = await CollectAsync(command);

        _resolvedProvider.Received(1).StreamChatAsync(
            Arg.Is<IReadOnlyList<ChatMessage>>(m => m.Any(msg => msg.Role == ChatRole.System && msg.Content.Contains("Relevant excerpt."))),
            "gpt-4.1", Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>());

        var finalChunk = chunks.Should().ContainSingle(c => c.RetrievalOutcome != null).Subject;
        finalChunk.RetrievalOutcome!.Type.Should().Be(RagRetrievalOutcomeType.Grounded);
        finalChunk.RetrievalOutcome.Citations.Should().ContainSingle().Which.DocumentTitle.Should().Be("Doc.pdf");
    }

    [Fact]
    public async Task Handle_ShouldNeverRetrieve_WhenNoKnowledgeBaseIsAttached()
    {
        _conversationKnowledgeBases.GetByConversationAsync(_chatId, Arg.Any<CancellationToken>())
            .Returns(new List<ConversationKnowledgeBase>());

        var command = new SendChatMessageCommand(_chatId, [new ChatMessageDto("user", "Hi")], _openAiProvider.Id, _gpt41.Id, null);

        var chunks = await CollectAsync(command);

        await _ragService.DidNotReceive().RetrieveContextAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());
        _resolvedProvider.Received(1).StreamChatAsync(
            Arg.Is<IReadOnlyList<ChatMessage>>(m => m.Count == 1 && m[0].Role == ChatRole.User),
            "gpt-4.1", Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>());
        chunks.Should().NotContain(c => c.RetrievalOutcome != null);
    }

    private async Task<List<ChatStreamChunk>> CollectAsync(SendChatMessageCommand command)
    {
        var chunks = new List<ChatStreamChunk>();
        await foreach (var chunk in _handler.Handle(command, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        return chunks;
    }

    private static async IAsyncEnumerable<StreamChunk> ToAsyncEnumerable(IEnumerable<StreamChunk> items)
    {
        foreach (var item in items)
        {
            yield return item;
        }

        await Task.CompletedTask;
    }
}
