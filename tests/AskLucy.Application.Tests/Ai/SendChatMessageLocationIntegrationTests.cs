using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Locations;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Chats;
using FluentAssertions;
using Hangfire;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Ai;

/// <summary>
/// specs/037-location-query-resolution T016 — <see cref="SendChatMessageCommandHandler"/>
/// appends a ConfirmationText chunk for non-NoIntent outcomes and populates
/// <see cref="ChatStreamChunk.ConfirmedLocation"/> on the trailing chunk when the
/// location resolution service returns Confirmed. NoIntent is transparent (no extra chunk).
/// Back-reference re-emits the existing active location without a geocoding call.
/// </summary>
public sealed class SendChatMessageLocationIntegrationTests
{
    private readonly IAIProvider _resolvedProvider = Substitute.For<IAIProvider>();
    private readonly IAIProviderResolver _resolver = Substitute.For<IAIProviderResolver>();
    private readonly IAIProviderRepository _providers = Substitute.For<IAIProviderRepository>();
    private readonly IAIModelRepository _models = Substitute.For<IAIModelRepository>();
    private readonly IConversationKnowledgeBaseRepository _conversationKnowledgeBases = Substitute.For<IConversationKnowledgeBaseRepository>();
    private readonly IRagService _ragService = Substitute.For<IRagService>();
    private readonly IMemoryService _memoryService = Substitute.For<IMemoryService>();
    private readonly ILocationResolutionService _locationResolutionService = Substitute.For<ILocationResolutionService>();
    private readonly IUserChatRepository _userChatRepository = Substitute.For<IUserChatRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly IBackgroundJobClient _backgroundJobClient = Substitute.For<IBackgroundJobClient>();
    private readonly SendChatMessageCommandHandler _handler;
    private readonly AIProvider _openAiProvider;
    private readonly AIModel _gpt41;
    private readonly Guid _chatId = Guid.NewGuid();

    public SendChatMessageLocationIntegrationTests()
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
            .Returns(ToAsyncEnumerable([new StreamChunk("Here is the location.")]));

        _conversationKnowledgeBases.GetByConversationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<AskLucy.Domain.Retrieval.ConversationKnowledgeBase>());

        _memoryService.RetrieveRelevantMemoriesAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MemoryRetrievalOutcome(MemoryRetrievalOutcomeType.NoneRelevant, null, [], null));

        _handler = new SendChatMessageCommandHandler(
            _resolver, _providers, _models, _conversationKnowledgeBases, _ragService, _memoryService,
            _locationResolutionService, _userChatRepository, _currentUser, _backgroundJobClient,
            Microsoft.Extensions.Options.Options.Create(new LocationResolutionOptions()),
            new SendChatMessageCommandValidator(_providers, _models));
    }

    [Fact]
    public async Task Handle_ShouldYieldConfirmationTextAndConfirmedLocation_WhenLocationResolutionConfirms()
    {
        var confirmedLocation = new ConfirmedLocationData(25.2048, 55.2708, "Dubai, UAE", 0.85);
        var locationOutcome = new LocationResolutionOutcome(
            LocationResolutionOutcomeType.Confirmed, confirmedLocation,
            "I've located Dubai, UAE and centred the viewer on it.");

        _locationResolutionService.ResolveAsync(Arg.Any<string?>(), _chatId, Arg.Any<string>(), Arg.Any<ActiveSiteLocation?>(), Arg.Any<CancellationToken>())
            .Returns(locationOutcome);

        var command = new SendChatMessageCommand(_chatId, [new ChatMessageDto("user", "Show me Dubai")], _openAiProvider.Id, _gpt41.Id, null);
        var chunks = await CollectAsync(command);

        chunks.Should().Contain(c => c.ContentDelta == "I've located Dubai, UAE and centred the viewer on it.");

        var trailingChunk = chunks.Should().ContainSingle(c => c.ConfirmedLocation != null).Subject;
        trailingChunk.ConfirmedLocation!.LocationName.Should().Be("Dubai, UAE");
        trailingChunk.ConfirmedLocation.Latitude.Should().Be(25.2048);
    }

    [Fact]
    public async Task Handle_ShouldNotYieldExtraChunk_WhenLocationResolutionIsNoIntent()
    {
        _locationResolutionService.ResolveAsync(Arg.Any<string?>(), _chatId, Arg.Any<string>(), Arg.Any<ActiveSiteLocation?>(), Arg.Any<CancellationToken>())
            .Returns(new LocationResolutionOutcome(LocationResolutionOutcomeType.NoIntent, null, null));

        var command = new SendChatMessageCommand(_chatId, [new ChatMessageDto("user", "What is the tallest building?")], _openAiProvider.Id, _gpt41.Id, null);
        var chunks = await CollectAsync(command);

        chunks.Should().NotContain(c => c.ConfirmedLocation != null);
        chunks.Where(c => c.ContentDelta != null).Should().ContainSingle()
            .Which.ContentDelta.Should().Be("Here is the location.");
    }

    [Fact]
    public async Task Handle_ShouldYieldConfirmationText_WhenLocationResolutionIsAmbiguous()
    {
        var locationOutcome = new LocationResolutionOutcome(
            LocationResolutionOutcomeType.Ambiguous, null,
            LocationConfirmationTemplates.Ambiguous);

        _locationResolutionService.ResolveAsync(Arg.Any<string?>(), _chatId, Arg.Any<string>(), Arg.Any<ActiveSiteLocation?>(), Arg.Any<CancellationToken>())
            .Returns(locationOutcome);

        var command = new SendChatMessageCommand(_chatId, [new ChatMessageDto("user", "Show me Springfield")], _openAiProvider.Id, _gpt41.Id, null);
        var chunks = await CollectAsync(command);

        chunks.Should().Contain(c => c.ContentDelta == LocationConfirmationTemplates.Ambiguous);
        chunks.Should().NotContain(c => c.ConfirmedLocation != null);
    }

    [Fact]
    public async Task Handle_ShouldYieldUnavailableText_WhenLocationResolutionTimesOut()
    {
        var locationOutcome = new LocationResolutionOutcome(
            LocationResolutionOutcomeType.Unavailable, null,
            LocationConfirmationTemplates.Unavailable);

        _locationResolutionService.ResolveAsync(Arg.Any<string?>(), _chatId, Arg.Any<string>(), Arg.Any<ActiveSiteLocation?>(), Arg.Any<CancellationToken>())
            .Returns(locationOutcome);

        var command = new SendChatMessageCommand(_chatId, [new ChatMessageDto("user", "Show me Dubai")], _openAiProvider.Id, _gpt41.Id, null);
        var chunks = await CollectAsync(command);

        chunks.Should().Contain(c => c.ContentDelta == LocationConfirmationTemplates.Unavailable);
    }

    // T025 — Phase 4 (US2): NoIntent integration scenarios
    [Fact]
    public async Task Handle_ShouldStillCallResolveAsync_EvenWhenNoIntent()
    {
        _locationResolutionService.ResolveAsync(Arg.Any<string?>(), _chatId, Arg.Any<string>(), Arg.Any<ActiveSiteLocation?>(), Arg.Any<CancellationToken>())
            .Returns(new LocationResolutionOutcome(LocationResolutionOutcomeType.NoIntent, null, null));

        var command = new SendChatMessageCommand(_chatId, [new ChatMessageDto("user", "compare parking ratios")], _openAiProvider.Id, _gpt41.Id, null);
        await CollectAsync(command);

        await _locationResolutionService.Received(1).ResolveAsync(
            Arg.Any<string?>(), _chatId, Arg.Any<string>(), Arg.Any<ActiveSiteLocation?>(), Arg.Any<CancellationToken>());
    }

    // T027 — Phase 5 (US3): NotFound integration scenario
    [Fact]
    public async Task Handle_ShouldYieldNotFoundText_WhenLocationResolutionIsNotFound()
    {
        var locationOutcome = new LocationResolutionOutcome(
            LocationResolutionOutcomeType.NotFound, null,
            LocationConfirmationTemplates.NotFound);

        _locationResolutionService.ResolveAsync(Arg.Any<string?>(), _chatId, Arg.Any<string>(), Arg.Any<ActiveSiteLocation?>(), Arg.Any<CancellationToken>())
            .Returns(locationOutcome);

        var command = new SendChatMessageCommand(_chatId, [new ChatMessageDto("user", "Show me Xyzzyplorp")], _openAiProvider.Id, _gpt41.Id, null);
        var chunks = await CollectAsync(command);

        chunks.Should().Contain(c => c.ContentDelta == LocationConfirmationTemplates.NotFound);
        chunks.Should().NotContain(c => c.ConfirmedLocation != null);
    }

    private async Task<List<ChatStreamChunk>> CollectAsync(SendChatMessageCommand command)
    {
        var chunks = new List<ChatStreamChunk>();
        await foreach (var chunk in _handler.Handle(command, CancellationToken.None))
            chunks.Add(chunk);
        return chunks;
    }

    private static async IAsyncEnumerable<StreamChunk> ToAsyncEnumerable(IEnumerable<StreamChunk> items)
    {
        foreach (var item in items) yield return item;
        await Task.CompletedTask;
    }
}
