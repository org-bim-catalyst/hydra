using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Runtime;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Flows;
using AskLucy.Application.Conversations.Runtime;
using AskLucy.Application.Locations;
using AskLucy.Application.Options;
using AskLucy.Application.Tests.SiteBoundaries;
using AskLucy.Domain.Chats;
using FluentAssertions;
using Hangfire;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Runtime;

/// <summary>
/// specs/083 — a request that could have meant "a picture" or "a map move" is answered with the
/// hand-composed card, and nothing runs until the user chooses.
/// </summary>
public sealed class MapSnapshotAmbiguityTurnTests
{
    private readonly IConversationKnowledgeBaseRepository _knowledgeBases = Substitute.For<IConversationKnowledgeBaseRepository>();
    private readonly IRagService _ragService = Substitute.For<IRagService>();
    private readonly IMemoryService _memoryService = Substitute.For<IMemoryService>();
    private readonly IUserChatRepository _userChatRepository = Substitute.For<IUserChatRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly IBackgroundJobClient _backgroundJobClient = Substitute.For<IBackgroundJobClient>();
    private readonly ITurnDecider _decider = Substitute.For<ITurnDecider>();
    private readonly IRetryTargetResolver _retryTargetResolver = Substitute.For<IRetryTargetResolver>();
    private readonly ISuggestedActionOfferGenerator _offerGenerator = Substitute.For<ISuggestedActionOfferGenerator>();
    private readonly IAIProvider _provider = Substitute.For<IAIProvider>();
    private readonly Guid _chatId = Guid.NewGuid();

    public MapSnapshotAmbiguityTurnTests()
    {
        _knowledgeBases.GetByConversationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<AskLucy.Domain.Retrieval.ConversationKnowledgeBase>());
        _memoryService.RetrieveRelevantMemoriesAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MemoryRetrievalOutcome(MemoryRetrievalOutcomeType.NoneRelevant, null, [], null));
        _userChatRepository.GetByIdAsync(_chatId, Arg.Any<CancellationToken>()).Returns((UserChat?)null);
        _currentUser.UserId.Returns("user-1");
    }

    private ConversationTurnOrchestrator BuildOrchestrator()
    {
        var toolCatalog = new AgentToolCatalog([], new EmptyMcpToolRegistry());
        var runtimeOptions = Microsoft.Extensions.Options.Options.Create(new ConversationRuntimeOptions());
        var indexRetriever = new CapabilityIndexRetriever(
            Substitute.For<IEmbeddingService>(), runtimeOptions, NullLogger<CapabilityIndexRetriever>.Instance);
        var capabilityCatalog = new ConversationCapabilityCatalog(toolCatalog, indexRetriever, runtimeOptions);

        var capabilityExecutor = new CapabilityExecutor(
            new AgentPolicyEvaluator(Substitute.For<IAgentPolicyRepository>()),
            new PermissiveSchemaValidator(),
            runtimeOptions,
            NullLogger<CapabilityExecutor>.Instance);

        var flowCatalog = new ConversationFlowCatalog([]);
        var narrator = new CapabilityNarrator(NullLogger<CapabilityNarrator>.Instance);
        var flowRunner = new FlowRunner(capabilityCatalog, capabilityExecutor, narrator, runtimeOptions, NullLogger<FlowRunner>.Instance);
        var subAgentDelegator = new SubAgentDelegator(
            TestServiceScopeFactory.Create(capabilityCatalog, capabilityExecutor),
            capabilityCatalog, narrator, runtimeOptions, NullLogger<SubAgentDelegator>.Instance);
        var turnRecorder = new TurnRecorder(
            Substitute.For<IAgentRepository>(), Substitute.For<IAgentExecutionRepository>(),
            Substitute.For<IUnitOfWork>(), NullLogger<TurnRecorder>.Instance);

        return new ConversationTurnOrchestrator(
            _knowledgeBases, Substitute.For<IMessageRepository>(), _ragService, _memoryService, _userChatRepository, _currentUser,
            _backgroundJobClient, capabilityCatalog, flowCatalog, _decider, _retryTargetResolver, capabilityExecutor, flowRunner, subAgentDelegator, _offerGenerator,
            narrator, turnRecorder, TestEffectiveSiteBoundary.None(), NullLogger<ConversationTurnOrchestrator>.Instance);
    }

    private ConversationTurnRequest Request(string message) =>
        new(_chatId, [new ChatMessageDto("user", message)], _provider, "test-model", GenerationParameters: null);

    [Fact]
    public async Task RunAsync_ShouldOfferTheCard_AndRunNothing()
    {
        var chunks = await CollectAsync(BuildOrchestrator(), Request("how does Al Safa Park 2 look"));

        var offer = chunks.Should().ContainSingle(c => c.SuggestedActions != null).Subject;
        offer.SuggestedActionsQuestion.Should().Contain("Al Safa Park 2");
        offer.SuggestedActions!.Select(a => a.Key).Should().Equal(
            MapSnapshotCapability.CapabilityKey,
            ResolveLocationCapability.CapabilityKey,
            null);

        // Nothing ran, so the turn's outcome says so — no capability may claim to have done
        // anything on the strength of a card the user has not yet answered.
        chunks.Should().ContainSingle(c => c.TurnOutcome != null)
            .Which.TurnOutcome!.Verdict.Should().Be(TurnVerdict.AnsweredInWords);
    }

    [Fact]
    public async Task RunAsync_ShouldNotDecide_AndNotRunAnything_BeforeTheUserChooses()
    {
        await CollectAsync(BuildOrchestrator(), Request("how does Al Safa Park 2 look"));

        // The whole reason this branch sits before the decide step: a decided "act" would have
        // moved the map already, and the card would then be offering a move that has happened.
        await _decider.DidNotReceive().DecideAsync(
            Arg.Any<TurnContext>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<CapabilityIndexEntry>>(),
            Arg.Any<IReadOnlyList<CapabilityIndexEntry>>(), Arg.Any<RecentTurnOutcomeSummary>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ShouldSayTheQuestionInWords_SoTheCardHasAMessageToRide()
    {
        // The controller persists an assistant message — and emits the offer — only when the turn
        // produced text. A card with nothing else on it would be dropped on the wire.
        var chunks = await CollectAsync(BuildOrchestrator(), Request("how does Al Safa Park 2 look"));

        chunks.Select(c => c.ContentDelta).Should().Contain(c => c != null && c.Contains("Al Safa Park 2"));
    }

    private static async Task<List<ChatStreamChunk>> CollectAsync(
        ConversationTurnOrchestrator orchestrator, ConversationTurnRequest request)
    {
        var chunks = new List<ChatStreamChunk>();
        await foreach (var chunk in orchestrator.RunAsync(request, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        return chunks;
    }

    private sealed class EmptyMcpToolRegistry : IMcpToolRegistry
    {
        public IReadOnlyCollection<IAgentTool> ActiveTools => [];

        public Task InvalidateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>Accepts everything — this test is about the turn's shape, not about grounding.</summary>
    private sealed class PermissiveSchemaValidator : IJsonSchemaValidator
    {
        public IReadOnlyList<string> Validate(JsonElement schema, JsonElement instance, long maxSizeBytes) => [];
    }
}
