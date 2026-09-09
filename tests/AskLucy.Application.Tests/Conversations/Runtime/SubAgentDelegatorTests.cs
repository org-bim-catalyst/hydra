using System.Text.Json;
using System.Text.RegularExpressions;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Runtime;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Runtime;
using AskLucy.Application.Locations;
using AskLucy.Application.Options;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Retrieval;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Runtime;

/// <summary>
/// specs/045 US5 (T099/T100), FR-016-FR-019 — <see cref="SubAgentDelegator"/> exercised against
/// real capabilities over faked backing services, the same style as
/// <see cref="Flows.LocateAPlaceFlowTests"/>. Covers: a two-area request whose independent slices
/// run concurrently; dependency ordering with the dependency's real result merged forward into
/// the dependent slice's arguments rather than re-derived; partial failure reported per slice; the
/// per-turn delegation cap; and that a delegated slice's own service scope is genuinely its own
/// (research.md D12).
/// </summary>
public sealed class SubAgentDelegatorTests
{
    private readonly ILocationResolutionService _locationService = Substitute.For<ILocationResolutionService>();
    private readonly IBoundaryResolutionService _boundaryService = Substitute.For<IBoundaryResolutionService>();
    private readonly IRagService _ragService = Substitute.For<IRagService>();
    private readonly IMemoryService _memoryService = Substitute.For<IMemoryService>();
    private readonly IAIProvider _provider = Substitute.For<IAIProvider>();
    private readonly IConversationKnowledgeBaseRepository _knowledgeBaseRepository = Substitute.For<IConversationKnowledgeBaseRepository>();
    private readonly Guid _chatId = Guid.NewGuid();

    public SubAgentDelegatorTests()
    {
        _knowledgeBaseRepository.GetByConversationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([ConversationKnowledgeBase.Create(_chatId, Guid.NewGuid(), "test")]);

        // Deterministic, inspectable narration — echoes the capability label, its outcome, and
        // any failure reason back out, the same fake LocateAPlaceFlowTests uses so this fixture
        // needs no knowledge of TurnNarrationPrompt's actual wording.
        _provider.ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<string>(), Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var systemPrompt = call.ArgAt<IReadOnlyList<ChatMessage>>(0)[0].Content;
                var label = Regex.Match(systemPrompt, "What ran: (.+)").Groups[1].Value;
                var succeeded = systemPrompt.Contains("Outcome: succeeded", StringComparison.Ordinal);
                var result = Regex.Match(systemPrompt, "Result: (.+)");
                var text = succeeded ? $"{label} done." : (result.Success ? result.Groups[1].Value : $"{label} failed.");
                return new ChatCompletionResult(text, new ChatUsage(null, null, null, null, null));
            });
    }

    private (SubAgentDelegator Delegator, IServiceScopeFactory ScopeFactory) BuildDelegator(int? maxDelegations = null)
    {
        var runtimeOptions = Microsoft.Extensions.Options.Options.Create(
            maxDelegations is { } max ? new ConversationRuntimeOptions { MaxDelegationsPerTurn = max } : new ConversationRuntimeOptions());

        var toolCatalog = new AgentToolCatalog(
            [
                new ResolveLocationCapability(_locationService),
                new ResolveSiteBoundaryCapability(_boundaryService, Substitute.For<IUserChatRepository>()),
                new SearchKnowledgeBaseCapability(_ragService, _knowledgeBaseRepository),
                new SearchMemoryCapability(_memoryService),
                new AdjustViewerFocusCapability(),
            ],
            new EmptyMcpToolRegistry());
        var capabilityCatalog = new ConversationCapabilityCatalog(
            toolCatalog,
            new CapabilityIndexRetriever(Substitute.For<IEmbeddingService>(), runtimeOptions, NullLogger<CapabilityIndexRetriever>.Instance),
            runtimeOptions);
        var capabilityExecutor = new CapabilityExecutor(
            new AgentPolicyEvaluator(Substitute.For<IAgentPolicyRepository>()), new PermissiveSchemaValidator(),
            runtimeOptions, NullLogger<CapabilityExecutor>.Instance);
        var narrator = new CapabilityNarrator(NullLogger<CapabilityNarrator>.Instance);

        var scopeFactory = new CountingServiceScopeFactory(TestServiceScopeFactory.Create(capabilityCatalog, capabilityExecutor));

        return (new SubAgentDelegator(scopeFactory, capabilityCatalog, narrator, runtimeOptions, NullLogger<SubAgentDelegator>.Instance), scopeFactory);
    }

    private ConversationTurnRequest Request() =>
        new(_chatId, [new ChatMessageDto("user", "find Al Safa Park 2 and check my documents about it")], _provider, "test-model", null);

    private static TurnContext Context() =>
        TurnContext.Empty("user-1", Guid.NewGuid()) with
        {
            GrantedPermissions = new HashSet<AgentToolPermission>
            {
                AgentToolPermission.ExternalNetwork, AgentToolPermission.ReadKnowledge, AgentToolPermission.ReadMemory,
            },
        };

    private static async Task<List<ChatStreamChunk>> CollectAsync(
        SubAgentDelegator delegator, ConversationTurnRequest request, IReadOnlyList<TurnSlice> slices, List<SubAgentDelegationResult> record)
    {
        var chunks = new List<ChatStreamChunk>();
        await foreach (var chunk in delegator.RunAsync(request, slices, Context(), record, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        return chunks;
    }

    [Fact]
    public async Task RunAsync_ShouldRunBothIndependentSlices_AcrossTwoDifferentAreas()
    {
        _ragService.RetrieveContextAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new RagRetrievalOutcome(RagRetrievalOutcomeType.Grounded, "Al Safa Park 2 is a public park.", [], null));
        _memoryService.RetrieveRelevantMemoriesAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MemoryRetrievalOutcome(MemoryRetrievalOutcomeType.Found, "The user visited this park before.", [], null));

        var (delegator, _) = BuildDelegator();
        var slices = new List<TurnSlice>
        {
            new(SearchKnowledgeBaseCapability.CapabilityKey, """{"query":"Al Safa Park 2","knowledgeBaseIds":["11111111-1111-1111-1111-111111111111"]}""", null, DependsOn: null),
            new(SearchMemoryCapability.CapabilityKey, """{"query":"Al Safa Park 2"}""", null, DependsOn: null),
        };
        var record = new List<SubAgentDelegationResult>();

        var chunks = await CollectAsync(delegator, Request(), slices, record);

        record.Should().HaveCount(2);
        record.Should().OnlyContain(r => r.Succeeded);
        record.Select(r => r.CapabilityKey).Should().BeEquivalentTo([SearchKnowledgeBaseCapability.CapabilityKey, SearchMemoryCapability.CapabilityKey]);

        var narrations = chunks.Where(c => c.ContentDelta is not null).Select(c => c.ContentDelta!).ToList();
        narrations.Should().Contain(n => n.Contains("Search my knowledge bases", StringComparison.Ordinal) && n.Contains("done.", StringComparison.Ordinal));
        narrations.Should().Contain(n => n.Contains("Check what I remember", StringComparison.Ordinal) && n.Contains("done.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_ShouldResolveEachSlice_InItsOwnServiceScope()
    {
        _ragService.RetrieveContextAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new RagRetrievalOutcome(RagRetrievalOutcomeType.Grounded, "context", [], null));
        _memoryService.RetrieveRelevantMemoriesAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MemoryRetrievalOutcome(MemoryRetrievalOutcomeType.Found, "context", [], null));

        var (delegator, scopeFactory) = BuildDelegator();
        var counting = (CountingServiceScopeFactory)scopeFactory;
        var slices = new List<TurnSlice>
        {
            new(SearchKnowledgeBaseCapability.CapabilityKey, """{"query":"x","knowledgeBaseIds":["11111111-1111-1111-1111-111111111111"]}""", null, DependsOn: null),
            new(SearchMemoryCapability.CapabilityKey, """{"query":"x"}""", null, DependsOn: null),
        };
        var record = new List<SubAgentDelegationResult>();

        await CollectAsync(delegator, Request(), slices, record);

        // research.md D12 — a scope per slice, never the request's own scope, so two slices racing
        // in the same wave can never share a scoped DbContext.
        counting.CreateScopeCallCount.Should().Be(2);
    }

    [Fact]
    public async Task RunAsync_ShouldMergeTheDependencysRealResult_IntoTheDependentSlicesArguments()
    {
        _locationService.ResolveQueryAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new LocationResolutionOutcome(LocationResolutionOutcomeType.Confirmed,
                new ConfirmedLocationData(25.15, 55.22, "Al Safa Park 2", 0.9), "confirmed"));
        _boundaryService.ResolveAsync(Arg.Any<ConfirmedLocationData>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new BoundaryResolutionOutcome(BoundaryResolutionOutcomeType.Confirmed, new ConfirmedSiteBoundaryData(
                "Al Safa Park 2", 25.15, 55.22, [], 42000, 0.7, BoundaryConfidenceLevel.Medium, SiteBoundarySource.OsmBoundary, "OpenStreetMap", []), null));

        var (delegator, _) = BuildDelegator();
        var slices = new List<TurnSlice>
        {
            new(ResolveLocationCapability.CapabilityKey, """{"query":"Al Safa Park 2"}""", null, DependsOn: null),
            // Deliberately empty — the only way this can succeed is if step 0's real
            // latitude/longitude/locationName were merged in (FR-017), mirroring exactly what
            // LocateAPlaceFlow.BindBoundaryArguments does by hand for its own fixed pair.
            new(ResolveSiteBoundaryCapability.CapabilityKey, "{}", null, DependsOn: 0),
        };
        var record = new List<SubAgentDelegationResult>();

        await CollectAsync(delegator, Request(), slices, record);

        record.Should().HaveCount(2);
        record.Should().OnlyContain(r => r.Succeeded, "the boundary slice's own required arguments were only ever supplied by the merge");
        await _boundaryService.Received(1).ResolveAsync(
            Arg.Is<ConfirmedLocationData>(l => l != null && l.LocationName == "Al Safa Park 2" && l.Latitude == 25.15),
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ShouldReportPartialFailure_WhenOneOfTwoIndependentSlicesFails()
    {
        _ragService.RetrieveContextAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new RagRetrievalOutcome(RagRetrievalOutcomeType.Grounded, "context", [], null));

        var (delegator, _) = BuildDelegator();
        var slices = new List<TurnSlice>
        {
            new(SearchKnowledgeBaseCapability.CapabilityKey, """{"query":"x","knowledgeBaseIds":["11111111-1111-1111-1111-111111111111"]}""", null, DependsOn: null),
            // No "query" — a deterministic, self-contained failure (FR-018's "partial failure").
            new(SearchMemoryCapability.CapabilityKey, "{}", null, DependsOn: null),
        };
        var record = new List<SubAgentDelegationResult>();

        var chunks = await CollectAsync(delegator, Request(), slices, record);

        record.Should().ContainSingle(r => r.CapabilityKey == SearchKnowledgeBaseCapability.CapabilityKey && r.Succeeded);
        record.Should().ContainSingle(r => r.CapabilityKey == SearchMemoryCapability.CapabilityKey && !r.Succeeded);

        var narrations = chunks.Where(c => c.ContentDelta is not null).Select(c => c.ContentDelta!).ToList();
        narrations.Should().Contain(n => n.Contains("Search my knowledge bases", StringComparison.Ordinal) && n.Contains("done.", StringComparison.Ordinal));
        narrations.Should().Contain(n => n.Contains("non-empty query is required", StringComparison.Ordinal),
            "the failed slice's own message names its cause, never the successful one's");
    }

    [Fact]
    public async Task RunAsync_ShouldDropSlicesBeyondTheDelegationCap()
    {
        _ragService.RetrieveContextAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new RagRetrievalOutcome(RagRetrievalOutcomeType.Grounded, "context", [], null));
        _memoryService.RetrieveRelevantMemoriesAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MemoryRetrievalOutcome(MemoryRetrievalOutcomeType.Found, "context", [], null));

        var (delegator, _) = BuildDelegator(maxDelegations: 1);
        var slices = new List<TurnSlice>
        {
            new(SearchKnowledgeBaseCapability.CapabilityKey, """{"query":"x","knowledgeBaseIds":["11111111-1111-1111-1111-111111111111"]}""", null, DependsOn: null),
            new(SearchMemoryCapability.CapabilityKey, """{"query":"x"}""", null, DependsOn: null),
        };
        var record = new List<SubAgentDelegationResult>();

        await CollectAsync(delegator, Request(), slices, record);

        record.Should().ContainSingle("FR-002's per-turn cap stops the turn with what it already achieved, never an unbounded fan-out");
        record[0].CapabilityKey.Should().Be(SearchKnowledgeBaseCapability.CapabilityKey);
    }

    [Fact]
    public async Task RunAsync_ShouldDropARepeatedIdenticalDelegation()
    {
        _ragService.RetrieveContextAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new RagRetrievalOutcome(RagRetrievalOutcomeType.Grounded, "context", [], null));

        var (delegator, _) = BuildDelegator();
        const string arguments = """{"query":"x","knowledgeBaseIds":["11111111-1111-1111-1111-111111111111"]}""";
        var slices = new List<TurnSlice>
        {
            new(SearchKnowledgeBaseCapability.CapabilityKey, arguments, null, DependsOn: null),
            new(SearchKnowledgeBaseCapability.CapabilityKey, arguments, null, DependsOn: null),
        };
        var record = new List<SubAgentDelegationResult>();

        await CollectAsync(delegator, Request(), slices, record);

        record.Should().ContainSingle("FR-019 — an exact repeat of the same capability and arguments within one turn is detected and dropped, not run twice");
    }

    [Fact]
    public void FindInArea_ShouldReturnNull_WhenTheCapabilityBelongsToADifferentArea()
    {
        var (delegator, _) = BuildDelegator();
        _ = delegator;

        var runtimeOptions = Microsoft.Extensions.Options.Options.Create(new ConversationRuntimeOptions());
        var catalog = new ConversationCapabilityCatalog(
            new AgentToolCatalog([new AdjustViewerFocusCapability()], new EmptyMcpToolRegistry()),
            new CapabilityIndexRetriever(Substitute.For<IEmbeddingService>(), runtimeOptions, NullLogger<CapabilityIndexRetriever>.Instance),
            runtimeOptions);

        // FR-016 — a knowledge sub-agent resolving strictly within its own area can never reach a
        // viewer capability, even by naming its exact key.
        catalog.FindInArea(SubAgentArea.Knowledge, AdjustViewerFocusCapability.CapabilityKey).Should().BeNull();
        catalog.FindInArea(SubAgentArea.Viewer, AdjustViewerFocusCapability.CapabilityKey).Should().NotBeNull();
    }

    private sealed class CountingServiceScopeFactory(IServiceScopeFactory inner) : IServiceScopeFactory
    {
        public int CreateScopeCallCount { get; private set; }

        public IServiceScope CreateScope()
        {
            CreateScopeCallCount++;
            return inner.CreateScope();
        }
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
