using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Prompts.Commands.ExecutePrompt;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Common;
using AskLucy.Domain.Prompts;
using FluentAssertions;
using FluentValidation;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Prompts;

/// <summary>
/// tasks.md T056/T057/T058. No live database/HTTP host is required or available in this
/// environment (see <c>CustomWebApplicationFactory</c>'s own documented no-live-database
/// constraint) — validation-blocks-before-any-provider-call (US2 AC1/SC-004) and a mid-stream
/// provider failure propagating uncaught (so the controller's own try/catch, verified by code
/// review, can turn it into an explicit SSE error event per FR-101/SC-010) are both fully
/// verifiable at the handler level with faked dependencies, consistent with
/// <c>SendChatMessageCommandHandler</c>'s own test style.
/// </summary>
public sealed class ExecutePromptCommandHandlerTests
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

    private static (Prompt Prompt, PromptVersion Version) BuildPrompt(PromptCapabilityRequirements? requiredCapabilities = null)
    {
        var content = new PromptContentSnapshot(
            "You are helpful.", null, "Summarize {{document}}.", null, null, null, null, null, null, null, null, false);
        var variables = new List<PromptVariableDefinition>
        {
            new("document", null, PromptVariableType.String, true, null, null, null, 0),
        };
        return Prompt.Create(
            OwnerId, $"Prompt {Guid.NewGuid():N}", null, PromptType.Chat, null, null,
            requiredCapabilities ?? PromptCapabilityRequirements.None, null, content, variables, OwnerId);
    }

    private static AIProvider BuildProvider() => AIProvider.Create("openai", "OpenAI", "system");

    private static AIModel BuildModel(Guid providerId, AIModelCapabilities? capabilities = null) => AIModel.Create(
        providerId, "gpt-5", "GPT-5", 128000, 4096,
        capabilities ?? new AIModelCapabilities(true, false, false, false, false, false, false, false, false),
        null, null, "system");

    private void SetUp(Prompt prompt, PromptVersion version, AIProvider provider, AIModel model)
    {
        _currentUser.UserId.Returns(OwnerId);
        _promptRepository.GetByIdForOwnerAsync(prompt.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(prompt);
        _promptRepository.GetVersionAsync(prompt.Id, version.VersionNumber, Arg.Any<CancellationToken>()).Returns(version);
        _providerRepository.GetByIdAsync(provider.Id, Arg.Any<CancellationToken>()).Returns(provider);
        _modelRepository.GetByIdAsync(model.Id, Arg.Any<CancellationToken>()).Returns(model);
        _providerResolver.Resolve(provider.ProviderKey).Returns(_aiProvider);
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
    public async Task Handle_ShouldBlockBeforeAnyProviderCall_WhenARequiredVariableIsMissing()
    {
        var (prompt, version) = BuildPrompt();
        var provider = BuildProvider();
        var model = BuildModel(provider.Id);
        SetUp(prompt, version, provider, model);

        var command = new ExecutePromptCommand(
            prompt.Id, null, new Dictionary<string, string?>(), provider.Id, model.Id, null, false, null, false);

        var act = async () =>
        {
            await foreach (var _ in CreateHandler().Handle(command, CancellationToken.None))
            {
            }
        };

        await act.Should().ThrowAsync<ValidationException>();
        _aiProvider.DidNotReceive().StreamChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<string>(), Arg.Any<GenerationParametersDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldBlockBeforeAnyProviderCall_WhenTheModelIsMissingARequiredCapability()
    {
        var (prompt, version) = BuildPrompt(new PromptCapabilityRequirements(
            RequiresStreaming: false, RequiresVision: true, RequiresFunctionCalling: false, RequiresJsonMode: false,
            RequiresReasoning: false, RequiresEmbeddings: false, RequiresImageInput: false, RequiresImageOutput: false, RequiresAudio: false));
        var provider = BuildProvider();
        var model = BuildModel(provider.Id); // vision = false by default in BuildModel
        SetUp(prompt, version, provider, model);

        var command = new ExecutePromptCommand(
            prompt.Id, null, new Dictionary<string, string?> { ["document"] = "text" }, provider.Id, model.Id, null, false, null, false);

        var act = async () =>
        {
            await foreach (var _ in CreateHandler().Handle(command, CancellationToken.None))
            {
            }
        };

        await act.Should().ThrowAsync<DomainRuleViolationException>();
        _aiProvider.DidNotReceive().StreamChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<string>(), Arg.Any<GenerationParametersDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldAssembleSystemThenResolvedUserMessage_AndStreamProviderContent()
    {
        var (prompt, version) = BuildPrompt();
        var provider = BuildProvider();
        var model = BuildModel(provider.Id);
        SetUp(prompt, version, provider, model);

        _aiProvider.StreamChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), model.ModelKey, Arg.Any<GenerationParametersDto>(), Arg.Any<CancellationToken>())
            .Returns(StreamOf(new StreamChunk("Hello", null), new StreamChunk(" world", new ChatUsage(10, 5, null, null, 120))));

        var command = new ExecutePromptCommand(
            prompt.Id, null, new Dictionary<string, string?> { ["document"] = "my report" }, provider.Id, model.Id, null, false, null, false);

        var chunks = new List<PromptStreamChunk>();
        await foreach (var chunk in CreateHandler().Handle(command, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        chunks.Select(c => c.ContentDelta).Should().Equal("Hello", " world", null);
        chunks[^1].ResolvedVariableValuesJson.Should().Contain("my report");

        _aiProvider.Received(1).StreamChatAsync(
            Arg.Is<IReadOnlyList<ChatMessage>>(messages =>
                messages.Count == 2 &&
                messages[0].Role == ChatRole.System && messages[0].Content == "You are helpful." &&
                messages[1].Role == ChatRole.User && messages[1].Content == "Summarize my report."),
            model.ModelKey, Arg.Any<GenerationParametersDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldPropagateAProviderFailure_UncaughtFromTheHandlerItself()
    {
        // Never caught inside the handler by design (C# iterators cannot wrap a yield in
        // try/catch) — the controller's own try/catch (verified by code review) is what turns
        // this into an explicit SSE error event, mirroring AiController.VoiceReply exactly.
        var (prompt, version) = BuildPrompt();
        var provider = BuildProvider();
        var model = BuildModel(provider.Id);
        SetUp(prompt, version, provider, model);

        _aiProvider.StreamChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), model.ModelKey, Arg.Any<GenerationParametersDto>(), Arg.Any<CancellationToken>())
            .Returns(ThrowingStream());

        var command = new ExecutePromptCommand(
            prompt.Id, null, new Dictionary<string, string?> { ["document"] = "text" }, provider.Id, model.Id, null, false, null, false);

        var act = async () =>
        {
            await foreach (var _ in CreateHandler().Handle(command, CancellationToken.None))
            {
            }
        };

        await act.Should().ThrowAsync<AiProviderUnavailableException>();
    }

    private static async IAsyncEnumerable<StreamChunk> ThrowingStream()
    {
        await Task.Yield();
        throw new AiProviderUnavailableException("Provider is down.");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }
}
