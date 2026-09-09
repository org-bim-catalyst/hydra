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
using AskLucy.Domain.Agents;
using AskLucy.Domain.Chats;
using AskLucy.Domain.Conversations;
using FluentAssertions;
using Hangfire;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Runtime;

/// <summary>
/// specs/045 US4 (T093), FR-051a — whether a mentioned place runs the <c>locate_a_place</c> flow,
/// is offered instead, or triggers nothing at all is decided entirely by the decide step's own
/// intent, never by a second classifier in the orchestrator. These tests substitute
/// <see cref="ITurnDecider"/> directly (the decide step's own prompt-level intent gating —
/// navigational vs. informational vs. passing-mention wording, and the borderline-resolves-to-
/// "suggest" rule — is <see cref="Prompts.TurnDecisionPromptTests"/>'s job) and ask only: given
/// each of the three verdicts, does the orchestrator do the right and only the right thing with it.
/// </summary>
public sealed class FlowIntentGatingTests
{
    private readonly IConversationKnowledgeBaseRepository _knowledgeBases = Substitute.For<IConversationKnowledgeBaseRepository>();
    private readonly IRagService _ragService = Substitute.For<IRagService>();
    private readonly IMemoryService _memoryService = Substitute.For<IMemoryService>();
    private readonly IUserChatRepository _userChatRepository = Substitute.For<IUserChatRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly IBackgroundJobClient _backgroundJobClient = Substitute.For<IBackgroundJobClient>();
    private readonly ITurnDecider _decider = Substitute.For<ITurnDecider>();
    private readonly ISuggestedActionOfferGenerator _offerGenerator = Substitute.For<ISuggestedActionOfferGenerator>();
    private readonly ILocationResolutionService _locationService = Substitute.For<ILocationResolutionService>();
    private readonly IAIProvider _provider = Substitute.For<IAIProvider>();
    private readonly Guid _chatId = Guid.NewGuid();

    public FlowIntentGatingTests()
    {
        _knowledgeBases.GetByConversationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<AskLucy.Domain.Retrieval.ConversationKnowledgeBase>());
        _memoryService.RetrieveRelevantMemoriesAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MemoryRetrievalOutcome(MemoryRetrievalOutcomeType.NoneRelevant, null, [], null));
        _userChatRepository.GetByIdAsync(_chatId, Arg.Any<CancellationToken>()).Returns((UserChat?)null);
        _currentUser.UserId.Returns("user-1");

        _locationService.ResolveQueryAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new LocationResolutionOutcome(LocationResolutionOutcomeType.Confirmed,
                new ConfirmedLocationData(25.15, 55.22, "Al Safa Park 2", 0.9), "confirmed"));

        _provider.StreamChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<string>(), Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable([new StreamChunk("Yes — it's a public park in Dubai.")]));
        _provider.ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<string>(), Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatCompletionResult("Location found. Now focusing the viewer on it.", new ChatUsage(null, null, null, null, null)));
    }

    private ConversationTurnOrchestrator BuildOrchestrator()
    {
        var toolCatalog = new AgentToolCatalog(
            [new ResolveLocationCapability(_locationService), new AdjustViewerFocusCapability(), new ResolveSiteBoundaryCapability(Substitute.For<AskLucy.Application.SiteBoundaries.IBoundaryResolutionService>())],
            new EmptyMcpToolRegistry());
        var runtimeOptions = Microsoft.Extensions.Options.Options.Create(new ConversationRuntimeOptions());
        var indexRetriever = new CapabilityIndexRetriever(
            Substitute.For<IEmbeddingService>(), runtimeOptions, NullLogger<CapabilityIndexRetriever>.Instance);
        var capabilityCatalog = new ConversationCapabilityCatalog(toolCatalog, indexRetriever, runtimeOptions);
        var capabilityExecutor = new CapabilityExecutor(
            new AgentPolicyEvaluator(Substitute.For<IAgentPolicyRepository>()), new PermissiveSchemaValidator(),
            runtimeOptions, NullLogger<CapabilityExecutor>.Instance);
        var flowCatalog = new ConversationFlowCatalog([new LocateAPlaceFlow()]);
        var flowRunner = new FlowRunner(capabilityCatalog, capabilityExecutor, runtimeOptions, NullLogger<FlowRunner>.Instance);

        return new ConversationTurnOrchestrator(
            _knowledgeBases, _ragService, _memoryService, _userChatRepository, _currentUser,
            _backgroundJobClient, capabilityCatalog, flowCatalog, _decider, capabilityExecutor, flowRunner, _offerGenerator,
            NullLogger<ConversationTurnOrchestrator>.Instance);
    }

    private ConversationTurnRequest Request(string message) =>
        new(_chatId, [new ChatMessageDto("user", message)], _provider, "test-model", GenerationParameters: null);

    private static async Task<List<ChatStreamChunk>> CollectAsync(ConversationTurnOrchestrator orchestrator, ConversationTurnRequest request)
    {
        var chunks = new List<ChatStreamChunk>();
        await foreach (var chunk in orchestrator.RunAsync(request, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        return chunks;
    }

    private static async IAsyncEnumerable<StreamChunk> ToAsyncEnumerable(IEnumerable<StreamChunk> chunks)
    {
        foreach (var chunk in chunks)
        {
            yield return chunk;
        }

        await Task.CompletedTask;
    }

    [Fact]
    public async Task NavigationalIntent_ShouldRunTheFlow_AndMoveTheViewer_WithNoOffer()
    {
        _decider.DecideAsync(Arg.Any<TurnContext>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<CapabilityIndexEntry>>(),
                Arg.Any<IReadOnlyList<CapabilityIndexEntry>>(), Arg.Any<CancellationToken>())
            .Returns(new TurnDecision(TurnIntent.Act, [], "locate_a_place", """{"query":"Al Safa Park 2"}""", ThroughStepIndex: null));

        var chunks = await CollectAsync(BuildOrchestrator(), Request("show me Al Safa Park 2"));

        chunks.Should().Contain(c => c.ConfirmedLocation != null, "the flow ran and moved the viewer");
        chunks.Should().NotContain(c => c.SuggestedActions != null, "a navigational request already got what it asked for — nothing to offer (FR-051a)");
        await _offerGenerator.DidNotReceive().GenerateAsync(
            Arg.Any<TurnContext>(), Arg.Any<TurnOutcome>(), Arg.Any<string>(), Arg.Any<string?>(),
            Arg.Any<IReadOnlyList<FlowVariantOfferCandidate>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InformationalIntent_ShouldAnswerInWords_OfferTheVariants_AndNeverMoveTheViewer()
    {
        _decider.DecideAsync(Arg.Any<TurnContext>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<CapabilityIndexEntry>>(),
                Arg.Any<IReadOnlyList<CapabilityIndexEntry>>(), Arg.Any<CancellationToken>())
            .Returns(new TurnDecision(TurnIntent.Suggest, [], "locate_a_place", """{"query":"Al Safa Park 2"}"""));
        _offerGenerator.GenerateAsync(Arg.Any<TurnContext>(), Arg.Any<TurnOutcome>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<IReadOnlyList<FlowVariantOfferCandidate>?>(), Arg.Any<CancellationToken>())
            .Returns(new SuggestedActionOffer("What would you like to do next?",
                [
                    new SuggestedAction(SuggestedActionKind.FlowVariant, "locate_a_place:full", null, "Focus and outline the site", "…", "{}"),
                    SuggestedAction.Decline(),
                ]));

        var chunks = await CollectAsync(BuildOrchestrator(), Request("do you know Al Safa Park 2?"));

        chunks.Select(c => c.ContentDelta).Should().Contain("Yes — it's a public park in Dubai.");
        chunks.Should().NotContain(c => c.ConfirmedLocation != null, "an informational turn never runs the flow — the viewer must not move");
        chunks.Should().ContainSingle(c => c.SuggestedActions != null)
            .Which.SuggestedActions!.Should().Contain(a => a.Key == "locate_a_place:full");

        // The candidate handed to the offer step must already carry the flow's own bound
        // arguments — never left for the offer-step model to invent (FR-051b's own contract).
        await _offerGenerator.Received(1).GenerateAsync(
            Arg.Any<TurnContext>(), Arg.Any<TurnOutcome>(), Arg.Any<string>(), Arg.Any<string?>(),
            Arg.Is<IReadOnlyList<FlowVariantOfferCandidate>?>(c => c != null && c.Any(v => v.CompoundKey == "locate_a_place:full" && v.ArgumentsJson.Contains("Al Safa Park 2"))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PassingMention_ShouldRunNothing_AndOfferNothing()
    {
        _decider.DecideAsync(Arg.Any<TurnContext>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<CapabilityIndexEntry>>(),
                Arg.Any<IReadOnlyList<CapabilityIndexEntry>>(), Arg.Any<CancellationToken>())
            .Returns(TurnDecision.AnswerOnly);

        var chunks = await CollectAsync(BuildOrchestrator(), Request("I read that Al Safa Park 2 was renovated"));

        chunks.Should().NotContain(c => c.ConfirmedLocation != null);
        chunks.Should().NotContain(c => c.SuggestedActions != null);
        await _offerGenerator.DidNotReceive().GenerateAsync(
            Arg.Any<TurnContext>(), Arg.Any<TurnOutcome>(), Arg.Any<string>(), Arg.Any<string?>(),
            Arg.Any<IReadOnlyList<FlowVariantOfferCandidate>?>(), Arg.Any<CancellationToken>());
    }

    private sealed class EmptyMcpToolRegistry : IMcpToolRegistry
    {
        public IReadOnlyCollection<IAgentTool> ActiveTools => [];

        public Task InvalidateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class PermissiveSchemaValidator : IJsonSchemaValidator
    {
        public IReadOnlyList<string> Validate(JsonElement schema, JsonElement instance, long maxSizeBytes) => [];
    }
}
