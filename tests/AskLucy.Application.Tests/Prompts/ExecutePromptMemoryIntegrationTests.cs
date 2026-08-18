using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Prompts.Commands.ExecutePrompt;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Prompts;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Prompts;

/// <summary>
/// tasks.md T103. Mirrors <c>ExecutePromptRagIntegrationTests</c> for
/// <see cref="IMemoryService"/>/`&lt;user_memory&gt;`, plus proves that when both RAG and Memory are
/// requested together the assembled message order matches research.md Decision 14 exactly:
/// system+developer instructions, then memory context, then RAG context, then the resolved user
/// instructions.
/// </summary>
public sealed class ExecutePromptMemoryIntegrationTests
{
    private const string OwnerId = "user-1";

    private readonly IPromptRepository _promptRepository = Substitute.For<IPromptRepository>();
    private readonly IAIProviderResolver _providerResolver = Substitute.For<IAIProviderResolver>();
    private readonly IAIProviderRepository _providerRepository = Substitute.For<IAIProviderRepository>();
    private readonly IAIModelRepository _modelRepository = Substitute.For<IAIModelRepository>();
    private readonly IRagService _ragService = Substitute.For<IRagService>();
    private readonly IMemoryService _memoryService = Substitute.For<IMemoryService>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly IAIProvider _aiProvider = Substitute.For<IAIProvider>();

    private ExecutePromptCommandHandler CreateHandler() =>
        new(_promptRepository, _providerResolver, _providerRepository, _modelRepository, _ragService, _memoryService, _currentUser);

    private static (Prompt Prompt, PromptVersion Version) BuildPrompt()
    {
        var content = new PromptContentSnapshot(
            "You are helpful.", "Be concise.", "Draft a status update.", null, null, null, null,
            null, null, null, null, false);
        return Prompt.Create(
            OwnerId, $"Prompt {Guid.NewGuid():N}", null, PromptType.Chat, null, null,
            PromptCapabilityRequirements.None, null, content, [], OwnerId);
    }

    private static AIProvider BuildProvider() => AIProvider.Create("openai", "OpenAI", "system");

    private static AIModel BuildModel(Guid providerId) => AIModel.Create(
        providerId, "gpt-5", "GPT-5", 128000, 4096,
        new AIModelCapabilities(true, false, false, false, false, false, false, false, false), null, null, "system");

    private void SetUp(Prompt prompt, PromptVersion version, AIProvider provider, AIModel model)
    {
        _currentUser.UserId.Returns(OwnerId);
        _promptRepository.GetByIdForOwnerAsync(prompt.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(prompt);
        _promptRepository.GetVersionAsync(prompt.Id, version.VersionNumber, Arg.Any<CancellationToken>()).Returns(version);
        _providerRepository.GetByIdAsync(provider.Id, Arg.Any<CancellationToken>()).Returns(provider);
        _modelRepository.GetByIdAsync(model.Id, Arg.Any<CancellationToken>()).Returns(model);
        _providerResolver.Resolve(provider.ProviderKey).Returns(_aiProvider);
        _aiProvider.StreamChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), model.ModelKey, Arg.Any<GenerationParametersDto>(), Arg.Any<CancellationToken>())
            .Returns(StreamOf(new StreamChunk("Update drafted.", null)));
    }

    private static async IAsyncEnumerable<StreamChunk> StreamOf(params StreamChunk[] chunks)
    {
        foreach (var chunk in chunks)
        {
            await Task.Yield();
            yield return chunk;
        }
    }

    [Fact]
    public async Task Handle_ShouldNotCallMemoryService_WhenUseMemoryContextIsFalse()
    {
        var (prompt, version) = BuildPrompt();
        var provider = BuildProvider();
        var model = BuildModel(provider.Id);
        SetUp(prompt, version, provider, model);

        var command = new ExecutePromptCommand(
            prompt.Id, null, new Dictionary<string, string?>(), provider.Id, model.Id, null,
            UseRagContext: false, KnowledgeBaseIds: null, UseMemoryContext: false);

        await foreach (var _ in CreateHandler().Handle(command, CancellationToken.None))
        {
        }

        await _memoryService.DidNotReceive().RetrieveRelevantMemoriesAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldCallMemoryService_WithAFreshCorrelationIdAndNullProject_WhenUseMemoryContextIsSet()
    {
        var (prompt, version) = BuildPrompt();
        var provider = BuildProvider();
        var model = BuildModel(provider.Id);
        SetUp(prompt, version, provider, model);

        _memoryService.RetrieveRelevantMemoriesAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MemoryRetrievalOutcome(MemoryRetrievalOutcomeType.NoneRelevant, null, [], null));

        var command = new ExecutePromptCommand(
            prompt.Id, null, new Dictionary<string, string?>(), provider.Id, model.Id, null,
            UseRagContext: false, KnowledgeBaseIds: null, UseMemoryContext: true);

        await foreach (var _ in CreateHandler().Handle(command, CancellationToken.None))
        {
        }

        await _memoryService.Received(1).RetrieveRelevantMemoriesAsync(
            OwnerId, Arg.Is<Guid>(id => id != prompt.Id && id != version.Id), null, "Draft a status update.", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldInsertAUserMemoryDelimitedSystemMessage_WhenMemoryIsFound()
    {
        var (prompt, version) = BuildPrompt();
        var provider = BuildProvider();
        var model = BuildModel(provider.Id);
        SetUp(prompt, version, provider, model);

        _memoryService.RetrieveRelevantMemoriesAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MemoryRetrievalOutcome(MemoryRetrievalOutcomeType.Found, "Prefers bullet points.", [], null));

        var command = new ExecutePromptCommand(
            prompt.Id, null, new Dictionary<string, string?>(), provider.Id, model.Id, null,
            UseRagContext: false, KnowledgeBaseIds: null, UseMemoryContext: true);

        var chunks = new List<PromptStreamChunk>();
        await foreach (var chunk in CreateHandler().Handle(command, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        _aiProvider.Received(1).StreamChatAsync(
            Arg.Is<IReadOnlyList<ChatMessage>>(messages =>
                messages.Any(m => m.Role == ChatRole.System && m.Content.Contains("<user_memory>") && m.Content.Contains("Prefers bullet points."))),
            model.ModelKey, Arg.Any<GenerationParametersDto>(), Arg.Any<CancellationToken>());

        chunks[^1].MemoryOutcome.Should().NotBeNull();
        chunks[^1].MemoryOutcome!.Type.Should().Be(MemoryRetrievalOutcomeType.Found);
    }

    [Fact]
    public async Task Handle_ShouldAssembleMessagesInDocumentedOrder_WhenBothRagAndMemoryAreRequested()
    {
        var (prompt, version) = BuildPrompt();
        var provider = BuildProvider();
        var model = BuildModel(provider.Id);
        SetUp(prompt, version, provider, model);

        _memoryService.RetrieveRelevantMemoriesAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MemoryRetrievalOutcome(MemoryRetrievalOutcomeType.Found, "Prefers bullet points.", [], null));
        _ragService.RetrieveContextAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new RagRetrievalOutcome(RagRetrievalOutcomeType.Grounded, "Q3 status doc.", [], null));

        var command = new ExecutePromptCommand(
            prompt.Id, null, new Dictionary<string, string?>(), provider.Id, model.Id, null,
            UseRagContext: true, KnowledgeBaseIds: [Guid.NewGuid()], UseMemoryContext: true);

        await foreach (var _ in CreateHandler().Handle(command, CancellationToken.None))
        {
        }

        _aiProvider.Received(1).StreamChatAsync(
            Arg.Is<IReadOnlyList<ChatMessage>>(messages =>
                messages.Count == 4 &&
                messages[0].Role == ChatRole.System && messages[0].Content.Contains("You are helpful.") && messages[0].Content.Contains("Be concise.") &&
                messages[1].Role == ChatRole.System && messages[1].Content.Contains("<user_memory>") &&
                messages[2].Role == ChatRole.System && messages[2].Content.Contains("<context>") &&
                messages[3].Role == ChatRole.User && messages[3].Content == "Draft a status update."),
            model.ModelKey, Arg.Any<GenerationParametersDto>(), Arg.Any<CancellationToken>());
    }
}
