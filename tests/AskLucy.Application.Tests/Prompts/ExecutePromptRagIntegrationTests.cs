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
/// tasks.md T102. <see cref="ExecutePromptCommandHandler"/> must call
/// <see cref="IRagService.RetrieveContextAsync"/> — passing a fresh <see cref="Guid"/> in the
/// `userChatId` slot (research.md Decision 3: a logging-only correlation id, never the real
/// conversation's id since this is a standalone Testing Workspace execution) — only when
/// <see cref="ExecutePromptCommand.UseRagContext"/> is set, and insert a `&lt;context&gt;`-delimited
/// system message (research.md Decision 14) when the outcome is grounded.
/// </summary>
public sealed class ExecutePromptRagIntegrationTests
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
            "You are helpful.", null, "Answer using only the provided documentation.", null, null, null, null,
            null, null, null, null, false);
        return Prompt.Create(
            OwnerId, $"Prompt {Guid.NewGuid():N}", null, PromptType.Rag, null, null,
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
            .Returns(StreamOf(new StreamChunk("Answer.", null)));
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
    public async Task Handle_ShouldNotCallRagService_WhenUseRagContextIsFalse()
    {
        var (prompt, version) = BuildPrompt();
        var provider = BuildProvider();
        var model = BuildModel(provider.Id);
        SetUp(prompt, version, provider, model);

        var command = new ExecutePromptCommand(
            prompt.Id, null, new Dictionary<string, string?>(), provider.Id, model.Id, null,
            UseRagContext: false, KnowledgeBaseIds: [Guid.NewGuid()], UseMemoryContext: false);

        await foreach (var _ in CreateHandler().Handle(command, CancellationToken.None))
        {
        }

        await _ragService.DidNotReceive().RetrieveContextAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldCallRagService_WithAFreshCorrelationId_NotThePromptOrVersionId_WhenUseRagContextIsSet()
    {
        var (prompt, version) = BuildPrompt();
        var provider = BuildProvider();
        var model = BuildModel(provider.Id);
        SetUp(prompt, version, provider, model);

        var knowledgeBaseId = Guid.NewGuid();
        _ragService.RetrieveContextAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new RagRetrievalOutcome(RagRetrievalOutcomeType.Grounded, "Q3 budget details.", [], null));

        var command = new ExecutePromptCommand(
            prompt.Id, null, new Dictionary<string, string?>(), provider.Id, model.Id, null,
            UseRagContext: true, KnowledgeBaseIds: [knowledgeBaseId], UseMemoryContext: false);

        await foreach (var _ in CreateHandler().Handle(command, CancellationToken.None))
        {
        }

        await _ragService.Received(1).RetrieveContextAsync(
            Arg.Is<Guid>(id => id != prompt.Id && id != version.Id),
            "Answer using only the provided documentation.",
            Arg.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == knowledgeBaseId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldInsertAContextDelimitedSystemMessage_WhenRetrievalIsGrounded()
    {
        var (prompt, version) = BuildPrompt();
        var provider = BuildProvider();
        var model = BuildModel(provider.Id);
        SetUp(prompt, version, provider, model);

        _ragService.RetrieveContextAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new RagRetrievalOutcome(RagRetrievalOutcomeType.Grounded, "Q3 budget details.", [], null));

        var command = new ExecutePromptCommand(
            prompt.Id, null, new Dictionary<string, string?>(), provider.Id, model.Id, null,
            UseRagContext: true, KnowledgeBaseIds: [Guid.NewGuid()], UseMemoryContext: false);

        var chunks = new List<PromptStreamChunk>();
        await foreach (var chunk in CreateHandler().Handle(command, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        _aiProvider.Received(1).StreamChatAsync(
            Arg.Is<IReadOnlyList<ChatMessage>>(messages =>
                messages.Any(m => m.Role == ChatRole.System && m.Content.Contains("<context>") && m.Content.Contains("Q3 budget details."))),
            model.ModelKey, Arg.Any<GenerationParametersDto>(), Arg.Any<CancellationToken>());

        chunks[^1].RetrievalOutcome.Should().NotBeNull();
        chunks[^1].RetrievalOutcome!.Type.Should().Be(RagRetrievalOutcomeType.Grounded);
    }
}
