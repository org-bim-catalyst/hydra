using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Runtime;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Conversations;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Runtime;
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
/// specs/045 US3 (T080) — <see cref="SelectedActionResolver"/> resolving and grounding a selection
/// at dispatch time (FR-027-FR-029): staleness, unknown vs. unavailable, and a successful match
/// never trusting the client's own arguments over the grounded row's.
/// </summary>
public sealed class SelectedActionResolverTests
{
    private readonly IMessageRepository _messages = Substitute.For<IMessageRepository>();
    private readonly IUserChatRepository _userChatRepository = Substitute.For<IUserChatRepository>();
    private readonly IConversationKnowledgeBaseRepository _knowledgeBases = Substitute.For<IConversationKnowledgeBaseRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly Guid _chatId = Guid.NewGuid();
    private readonly SelectedActionResolver _resolver;

    public SelectedActionResolverTests()
    {
        _knowledgeBases.GetByConversationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<AskLucy.Domain.Retrieval.ConversationKnowledgeBase>());
        _userChatRepository.GetByIdAsync(_chatId, Arg.Any<CancellationToken>()).Returns((UserChat?)null);
        _currentUser.UserId.Returns("user-1");

        var toolCatalog = new AgentToolCatalog([new StubCapability()], new EmptyMcpToolRegistry());
        var runtimeOptions = Microsoft.Extensions.Options.Options.Create(new ConversationRuntimeOptions());
        var indexRetriever = new CapabilityIndexRetriever(
            Substitute.For<IEmbeddingService>(), runtimeOptions, NullLogger<CapabilityIndexRetriever>.Instance);
        var catalog = new ConversationCapabilityCatalog(toolCatalog, indexRetriever, runtimeOptions);

        _resolver = new SelectedActionResolver(_messages, _userChatRepository, _knowledgeBases, _currentUser, catalog);
    }

    private Message OfferingMessage(SuggestedActionOffer offer, DateTime createdAtUtc)
    {
        var message = Message.Create(
            _chatId, MessageRole.Assistant, MessageKind.Text, "What would you like to do next?", null, "assistant",
            suggestedActionsJson: JsonSerializer.Serialize(offer, SuggestedActionJson.Options));
        message.CreatedAtUtc = createdAtUtc;
        return message;
    }

    private static SuggestedActionOffer StubOffer() => new(
        "What would you like to do next?",
        [
            new SuggestedAction(SuggestedActionKind.Capability, "stub", null, "Run the stub", "Does the stub thing.", """{"query":"x"}"""),
            new SuggestedAction(SuggestedActionKind.FollowUp, null, "Tell me more about it", "Tell me more", "More detail.", null),
            SuggestedAction.Decline(),
        ]);

    [Fact]
    public async Task ResolveAsync_ShouldRefuseStale_WhenTheOfferingMessageDoesNotExist()
    {
        _messages.ListByChatIdAsync(_chatId, Arg.Any<CancellationToken>()).Returns(new List<Message>());

        var act = () => _resolver.ResolveAsync(_chatId, Guid.NewGuid(), "capability", "stub", null, "{}", CancellationToken.None);

        await act.Should().ThrowAsync<ConversationActionStaleException>();
    }

    [Fact]
    public async Task ResolveAsync_ShouldRefuseStale_WhenANewerOfferHasSupersededIt()
    {
        var older = OfferingMessage(StubOffer(), DateTime.UtcNow.AddMinutes(-5));
        var newer = OfferingMessage(StubOffer(), DateTime.UtcNow);
        _messages.ListByChatIdAsync(_chatId, Arg.Any<CancellationToken>()).Returns(new List<Message> { older, newer });

        var act = () => _resolver.ResolveAsync(_chatId, older.Id, "capability", "stub", null, "{}", CancellationToken.None);

        await act.Should().ThrowAsync<ConversationActionStaleException>();
    }

    [Fact]
    public async Task ResolveAsync_ShouldRefuseStale_WhenNoRowMatchesTheSelection()
    {
        var offering = OfferingMessage(StubOffer(), DateTime.UtcNow);
        _messages.ListByChatIdAsync(_chatId, Arg.Any<CancellationToken>()).Returns(new List<Message> { offering });

        var act = () => _resolver.ResolveAsync(_chatId, offering.Id, "capability", "not_in_the_offer", null, "{}", CancellationToken.None);

        await act.Should().ThrowAsync<ConversationActionStaleException>();
    }

    [Fact]
    public async Task ResolveAsync_ShouldRefuseUnknown_ForAnUnregisteredCapabilityKey()
    {
        var offer = new SuggestedActionOffer("…", [
            new SuggestedAction(SuggestedActionKind.Capability, "ghost_capability", null, "Ghost", "…", "{}"),
            SuggestedAction.Decline(),
        ]);
        var offering = OfferingMessage(offer, DateTime.UtcNow);
        _messages.ListByChatIdAsync(_chatId, Arg.Any<CancellationToken>()).Returns(new List<Message> { offering });

        var act = () => _resolver.ResolveAsync(_chatId, offering.Id, "capability", "ghost_capability", null, "{}", CancellationToken.None);

        await act.Should().ThrowAsync<ConversationActionUnknownException>();
    }

    [Fact]
    public async Task ResolveAsync_ShouldRefuseUnknown_ForAFlowVariantRow()
    {
        // specs/045 Phase 4/6 — no flow registry exists yet, so any flowVariant row is always
        // ungrounded, the same as a hallucinated key.
        var offer = new SuggestedActionOffer("…", [
            new SuggestedAction(SuggestedActionKind.FlowVariant, "locate_a_place:full", null, "Focus and outline", "…", "{}"),
            SuggestedAction.Decline(),
        ]);
        var offering = OfferingMessage(offer, DateTime.UtcNow);
        _messages.ListByChatIdAsync(_chatId, Arg.Any<CancellationToken>()).Returns(new List<Message> { offering });

        var act = () => _resolver.ResolveAsync(_chatId, offering.Id, "flowVariant", "locate_a_place:full", null, "{}", CancellationToken.None);

        await act.Should().ThrowAsync<ConversationActionUnknownException>();
    }

    [Fact]
    public async Task ResolveAsync_ShouldRefuseUnavailable_WhenTheCapabilityCanNoLongerRun()
    {
        var offer = new SuggestedActionOffer("…", [
            new SuggestedAction(SuggestedActionKind.Capability, "unavailable_now", null, "Do it", "…", "{}"),
            SuggestedAction.Decline(),
        ]);
        var offering = OfferingMessage(offer, DateTime.UtcNow);
        _messages.ListByChatIdAsync(_chatId, Arg.Any<CancellationToken>()).Returns(new List<Message> { offering });

        var toolCatalog = new AgentToolCatalog([new StubCapability { Available = false, Key = "unavailable_now" }], new EmptyMcpToolRegistry());
        var runtimeOptions = Microsoft.Extensions.Options.Options.Create(new ConversationRuntimeOptions());
        var indexRetriever = new CapabilityIndexRetriever(
            Substitute.For<IEmbeddingService>(), runtimeOptions, NullLogger<CapabilityIndexRetriever>.Instance);
        var catalog = new ConversationCapabilityCatalog(toolCatalog, indexRetriever, runtimeOptions);
        var resolver = new SelectedActionResolver(_messages, _userChatRepository, _knowledgeBases, _currentUser, catalog);

        var act = () => resolver.ResolveAsync(_chatId, offering.Id, "capability", "unavailable_now", null, "{}", CancellationToken.None);

        await act.Should().ThrowAsync<ConversationActionUnavailableException>();
    }

    [Fact]
    public async Task ResolveAsync_ShouldResolve_AValidCapabilitySelection_UsingTheGroundedArguments_NotTheClients()
    {
        var offering = OfferingMessage(StubOffer(), DateTime.UtcNow);
        _messages.ListByChatIdAsync(_chatId, Arg.Any<CancellationToken>()).Returns(new List<Message> { offering });

        // The client's own arguments must never override what was actually grounded and offered.
        var resolved = await _resolver.ResolveAsync(
            _chatId, offering.Id, "capability", "stub", null, """{"query":"something the client made up"}""", CancellationToken.None);

        resolved.Row.Key.Should().Be("stub");
        resolved.Row.ArgumentsJson.Should().Be("""{"query":"x"}""");
        resolved.OfferingMessageId.Should().Be(offering.Id);
    }

    [Fact]
    public async Task ResolveAsync_ShouldResolve_AFollowUpSelection_MatchedByText()
    {
        var offering = OfferingMessage(StubOffer(), DateTime.UtcNow);
        _messages.ListByChatIdAsync(_chatId, Arg.Any<CancellationToken>()).Returns(new List<Message> { offering });

        var resolved = await _resolver.ResolveAsync(
            _chatId, offering.Id, "followUp", null, "Tell me more about it", "{}", CancellationToken.None);

        resolved.Row.Kind.Should().Be(SuggestedActionKind.FollowUp);
        resolved.Row.Text.Should().Be("Tell me more about it");
    }

    [Fact]
    public async Task ResolveAsync_ShouldResolve_ADeclineSelection_WithNoAvailabilityCheck()
    {
        var offering = OfferingMessage(StubOffer(), DateTime.UtcNow);
        _messages.ListByChatIdAsync(_chatId, Arg.Any<CancellationToken>()).Returns(new List<Message> { offering });

        // No knowledge-base/chat lookup should even be needed for a decline — nothing to re-check.
        var resolved = await _resolver.ResolveAsync(_chatId, offering.Id, "decline", null, null, "{}", CancellationToken.None);

        resolved.Row.IsDecline.Should().BeTrue();
        await _knowledgeBases.DidNotReceive().GetByConversationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    private sealed class StubCapability : IConversationCapability
    {
        public bool Available { get; init; } = true;

        public string Key { get; init; } = "stub";

        public string Name => Key;

        public string Description => "A stub capability.";

        public string WhenToUse => "Use when a test needs a capability to exist.";

        public string ArgumentHint => "a query";

        public string UsageGuidance => "Report what came back.";

        public string Label => "Stub";

        public string OfferDescription => "Runs the stub.";

        public string AcknowledgementTemplate => "Running the stub.";

        public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Low;

        public IReadOnlyList<AgentToolPermission> RequiredPermissions => [];

        public string InputSchemaJson => """{"type":"object"}""";

        public string OutputSchemaJson => """{"type":"object"}""";

        public CapabilityDuration ExpectedDuration => CapabilityDuration.Brief;

        public bool IsAvailable(TurnContext context) => Available;

        public Task<AgentToolResult> ExecuteAsync(
            AgentToolExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default) =>
            Task.FromResult(AgentToolResult.Success(JsonSerializer.SerializeToDocument(new { status = "done" })));
    }

    private sealed class EmptyMcpToolRegistry : IMcpToolRegistry
    {
        public IReadOnlyCollection<IAgentTool> ActiveTools => [];

        public Task InvalidateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

/// <summary>
/// specs/045 US3 (T080) — dispatching an already-resolved selection through
/// <see cref="ConversationTurnOrchestrator"/> runs exactly that, with no decide step in between
/// (FR-027), and a <c>followUp</c> selection invokes zero capabilities (SC-002a).
/// </summary>
public sealed class SelectedActionDispatchOrchestratorTests
{
    private readonly IConversationKnowledgeBaseRepository _knowledgeBases = Substitute.For<IConversationKnowledgeBaseRepository>();
    private readonly IRagService _ragService = Substitute.For<IRagService>();
    private readonly IMemoryService _memoryService = Substitute.For<IMemoryService>();
    private readonly IUserChatRepository _userChatRepository = Substitute.For<IUserChatRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly IBackgroundJobClient _backgroundJobClient = Substitute.For<IBackgroundJobClient>();
    private readonly ITurnDecider _decider = Substitute.For<ITurnDecider>();
    private readonly ISuggestedActionOfferGenerator _offerGenerator = Substitute.For<ISuggestedActionOfferGenerator>();
    private readonly IAIProvider _provider = Substitute.For<IAIProvider>();
    private readonly Guid _chatId = Guid.NewGuid();

    public SelectedActionDispatchOrchestratorTests()
    {
        _knowledgeBases.GetByConversationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<AskLucy.Domain.Retrieval.ConversationKnowledgeBase>());
        _memoryService.RetrieveRelevantMemoriesAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MemoryRetrievalOutcome(MemoryRetrievalOutcomeType.NoneRelevant, null, [], null));
        _userChatRepository.GetByIdAsync(_chatId, Arg.Any<CancellationToken>()).Returns((UserChat?)null);
        _currentUser.UserId.Returns("user-1");
    }

    private ConversationTurnOrchestrator BuildOrchestrator(IConversationCapability? capability = null)
    {
        var toolCatalog = new AgentToolCatalog(capability is null ? [] : [capability], new EmptyMcpToolRegistry());
        var runtimeOptions = Microsoft.Extensions.Options.Options.Create(new ConversationRuntimeOptions());
        var indexRetriever = new CapabilityIndexRetriever(
            Substitute.For<IEmbeddingService>(), runtimeOptions, NullLogger<CapabilityIndexRetriever>.Instance);
        var capabilityCatalog = new ConversationCapabilityCatalog(toolCatalog, indexRetriever, runtimeOptions);

        var capabilityExecutor = new CapabilityExecutor(
            new AgentPolicyEvaluator(Substitute.For<IAgentPolicyRepository>()),
            new PermissiveSchemaValidator(),
            NullLogger<CapabilityExecutor>.Instance);

        return new ConversationTurnOrchestrator(
            _knowledgeBases, _ragService, _memoryService, _userChatRepository, _currentUser,
            _backgroundJobClient, capabilityCatalog, _decider, capabilityExecutor, _offerGenerator,
            NullLogger<ConversationTurnOrchestrator>.Instance);
    }

    private ConversationTurnRequest Request(SelectedActionInput selection) =>
        new(_chatId, [new ChatMessageDto("user", "Run the stub")], _provider, "test-model", null, selection);

    private static async Task<List<ChatStreamChunk>> CollectAsync(ConversationTurnOrchestrator orchestrator, ConversationTurnRequest request)
    {
        var chunks = new List<ChatStreamChunk>();
        await foreach (var chunk in orchestrator.RunAsync(request, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        return chunks;
    }

    [Fact]
    public async Task RunAsync_ShouldDispatchTheCapability_WithNoDecideStepCall()
    {
        var capability = new StubCapability();
        var selection = new SelectedActionInput(Guid.NewGuid(), SuggestedActionKind.Capability, "stub", null, "{}");
        _provider.ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<string>(), Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatCompletionResult("Done.", new ChatUsage(null, null, null, null, null)));

        var chunks = await CollectAsync(BuildOrchestrator(capability), Request(selection));

        capability.WasInvoked.Should().BeTrue();
        chunks.Select(c => c.ContentDelta).Should().Contain("Done.");

        // FR-027 — "the orchestrator skips the decision step and runs the selection directly".
        await _decider.DidNotReceive().DecideAsync(
            Arg.Any<TurnContext>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<CapabilityIndexEntry>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ShouldRunNoCapability_ForAFollowUpSelection()
    {
        // SC-002a — selecting a followUp invokes zero capabilities, structurally, not just by convention.
        var capability = new StubCapability();
        var selection = new SelectedActionInput(Guid.NewGuid(), SuggestedActionKind.FollowUp, null, "Tell me more about it", "{}");
        _provider.StreamChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<string>(), Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable([new StreamChunk("Sure — here's more about it.")]));

        var chunks = await CollectAsync(BuildOrchestrator(capability), Request(selection));

        capability.WasInvoked.Should().BeFalse();
        chunks.Select(c => c.ContentDelta).Should().Contain("Sure — here's more about it.");
        chunks.Should().NotContain(c => c.StartsNewMessage, "a followUp reply carries no beats");
    }

    [Fact]
    public async Task RunAsync_ShouldPerformNoWork_ForADeclineSelection()
    {
        var capability = new StubCapability();
        var selection = new SelectedActionInput(Guid.NewGuid(), SuggestedActionKind.Decline, null, null, "{}");

        var chunks = await CollectAsync(BuildOrchestrator(capability), Request(selection));

        capability.WasInvoked.Should().BeFalse();
        chunks.Should().ContainSingle();
        chunks[0].SuggestedActions.Should().BeNull("FR-025a.3 — a decline must not be immediately re-offered");
    }

    [Fact]
    public async Task RunAsync_ShouldNotCallTheOfferGenerator_AfterADeclineSelection()
    {
        var selection = new SelectedActionInput(Guid.NewGuid(), SuggestedActionKind.Decline, null, null, "{}");

        await CollectAsync(BuildOrchestrator(), Request(selection));

        await _offerGenerator.DidNotReceive().GenerateAsync(
            Arg.Any<TurnContext>(), Arg.Any<TurnOutcome>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    private static async IAsyncEnumerable<StreamChunk> ToAsyncEnumerable(IEnumerable<StreamChunk> chunks)
    {
        foreach (var chunk in chunks)
        {
            yield return chunk;
        }

        await Task.CompletedTask;
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

    private sealed class StubCapability : IConversationCapability
    {
        public bool WasInvoked { get; private set; }

        public string Name => "stub";

        public string Description => "A stub capability.";

        public string WhenToUse => "Use when a test needs a capability to exist.";

        public string ArgumentHint => "anything";

        public string UsageGuidance => "Report what came back.";

        public string Label => "Stub";

        public string OfferDescription => "A stub.";

        public string AcknowledgementTemplate => "OK, doing the thing.";

        public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Low;

        public IReadOnlyList<AgentToolPermission> RequiredPermissions => [];

        public string InputSchemaJson => """{"type":"object"}""";

        public string OutputSchemaJson => """{"type":"object"}""";

        public CapabilityDuration ExpectedDuration => CapabilityDuration.Brief;

        public bool IsAvailable(TurnContext context) => true;

        public Task<AgentToolResult> ExecuteAsync(
            AgentToolExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
        {
            WasInvoked = true;
            return Task.FromResult(AgentToolResult.Success(JsonDocument.Parse("{\"status\":\"done\"}")));
        }
    }
}
