using System.Text.Json;
using System.Text.RegularExpressions;
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
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.Chats;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Flows;

/// <summary>
/// specs/045 US4 (T092), contracts/capability-flow.md's own testing contract — the
/// <c>locate_a_place</c> flow run through the real <see cref="FlowRunner"/>, against real
/// <see cref="ResolveLocationCapability"/>/<see cref="AdjustViewerFocusCapability"/>/
/// <see cref="ResolveSiteBoundaryCapability"/> instances over faked backing services.
/// </summary>
public sealed class LocateAPlaceFlowTests
{
    private readonly ILocationResolutionService _locationService = Substitute.For<ILocationResolutionService>();
    private readonly IBoundaryResolutionService _boundaryService = Substitute.For<IBoundaryResolutionService>();
    private readonly IAIProvider _provider = Substitute.For<IAIProvider>();
    private readonly Guid _chatId = Guid.NewGuid();
    private readonly LocateAPlaceFlow _flow = new();
    private readonly FlowRunner _runner;

    public LocateAPlaceFlowTests()
    {
        var runtimeOptions = Microsoft.Extensions.Options.Options.Create(new ConversationRuntimeOptions());
        var capabilityCatalog = new ConversationCapabilityCatalog(
            new AgentToolCatalog(
                [new ResolveLocationCapability(_locationService), new AdjustViewerFocusCapability(), new ResolveSiteBoundaryCapability(_boundaryService)],
                new EmptyMcpToolRegistry()),
            new CapabilityIndexRetriever(Substitute.For<IEmbeddingService>(), runtimeOptions, NullLogger<CapabilityIndexRetriever>.Instance),
            runtimeOptions);
        var capabilityExecutor = new CapabilityExecutor(
            new AgentPolicyEvaluator(Substitute.For<IAgentPolicyRepository>()), new PermissiveSchemaValidator(),
            runtimeOptions, NullLogger<CapabilityExecutor>.Instance);
        var narrator = new CapabilityNarrator(NullLogger<CapabilityNarrator>.Instance);
        _runner = new FlowRunner(capabilityCatalog, capabilityExecutor, narrator, runtimeOptions, NullLogger<FlowRunner>.Instance);

        // Deterministic, inspectable narration: echoes the capability label, its outcome, and any
        // "moving on to" clause back out — enough to assert the N+1 pairing without needing this
        // fixture to reproduce TurnNarrationPrompt's actual wording (that prompt's own tests own that).
        _provider.ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<string>(), Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var systemPrompt = call.ArgAt<IReadOnlyList<ChatMessage>>(0)[0].Content;
                var label = Regex.Match(systemPrompt, "What ran: (.+)").Groups[1].Value;
                var succeeded = systemPrompt.Contains("Outcome: succeeded", StringComparison.Ordinal);
                var next = Regex.Match(systemPrompt, "moving on to: (.+)\\. One short clause");
                var result = Regex.Match(systemPrompt, "Result: (.+)");
                var text = succeeded ? $"{label} done." : (result.Success ? result.Groups[1].Value : $"{label} failed.");
                if (next.Success)
                {
                    text += $" {next.Groups[1].Value}";
                }

                return new ChatCompletionResult(text, new ChatUsage(null, null, null, null, null));
            });
    }

    private ConversationTurnRequest Request() =>
        new(_chatId, [new ChatMessageDto("user", "show me Al Safa Park 2")], _provider, "test-model", null);

    private static TurnContext Context(AskLucy.Domain.Chats.ActiveSiteLocation? location = null, ActiveSiteBoundary? boundary = null) =>
        TurnContext.Empty("user-1", Guid.NewGuid()) with
        {
            ActiveLocation = location,
            ActiveBoundary = boundary,
            GrantedPermissions = new HashSet<AgentToolPermission> { AgentToolPermission.ExternalNetwork },
        };

    private void SucceedLocation(string name = "Al Safa Park 2", double lat = 25.15, double lon = 55.22, double confidence = 0.9) =>
        _locationService.ResolveQueryAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new LocationResolutionOutcome(LocationResolutionOutcomeType.Confirmed, new ConfirmedLocationData(lat, lon, name, confidence), "confirmed"));

    private void SucceedBoundary(string siteName = "Al Safa Park 2") =>
        _boundaryService.ResolveAsync(Arg.Any<ConfirmedLocationData>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new BoundaryResolutionOutcome(BoundaryResolutionOutcomeType.Confirmed, new ConfirmedSiteBoundaryData(
                siteName, 25.15, 55.22, [new GeoPoint(25.1, 55.2), new GeoPoint(25.2, 55.3)], 42000, 0.7,
                BoundaryConfidenceLevel.Medium, SiteBoundarySource.OsmBoundary, "OpenStreetMap", []), null));

    private async Task<List<ChatStreamChunk>> RunAsync(TurnContext context, int throughStepIndex, List<FlowStepResult> record) =>
        await CollectAsync(_runner, Request(), throughStepIndex, context, record);

    private static async Task<List<ChatStreamChunk>> CollectAsync(
        FlowRunner runner, ConversationTurnRequest request, int throughStepIndex, TurnContext context, List<FlowStepResult> record)
    {
        var chunks = new List<ChatStreamChunk>();
        await foreach (var chunk in runner.RunAsync(request, new LocateAPlaceFlow(), throughStepIndex, context, """{"query":"Al Safa Park 2"}""", record, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        return chunks;
    }

    [Fact]
    public async Task RunAsync_ShouldRunAllThreeSteps_WithFourMessages_AndNeverReGeocode()
    {
        SucceedLocation();
        SucceedBoundary();
        var record = new List<FlowStepResult>();

        var chunks = await RunAsync(Context(), throughStepIndex: 2, record);

        var messages = chunks.Where(c => c.ContentDelta is not null).Select(c => c.ContentDelta!).ToList();
        messages.Should().HaveCount(4, "an N=3 step flow produces N+1 messages (FR-052/FR-053)");
        messages[0].Should().Be("Looking for it.");
        messages[1].Should().Contain("done.").And.Contain("Now focusing the viewer on it.");
        messages[2].Should().Contain("done.").And.Contain("Now highlighting the boundary.");
        messages[3].Should().Contain("done.");

        await _locationService.Received(1).ResolveQueryAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        record.Should().HaveCount(3);
        record.Should().OnlyContain(r => r.Attempted && r.Succeeded);
    }

    [Fact]
    public async Task RunAsync_ShouldEmitTheConfirmedLocation_AfterStep1()
    {
        SucceedLocation(name: "Al Safa Park 2");
        SucceedBoundary();
        var record = new List<FlowStepResult>();

        var chunks = await RunAsync(Context(), throughStepIndex: 2, record);

        chunks.Should().ContainSingle(c => c.ConfirmedLocation != null)
            .Which.ConfirmedLocation!.LocationName.Should().Be("Al Safa Park 2");
    }

    [Fact]
    public async Task RunAsync_ShouldStopAndNameOnlyTheCause_WhenStep1Fails()
    {
        _locationService.ResolveQueryAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new LocationResolutionOutcome(LocationResolutionOutcomeType.NotFound, null, "I couldn't find a place matching that name."));
        var record = new List<FlowStepResult>();

        var chunks = await RunAsync(Context(), throughStepIndex: 2, record);

        var messages = chunks.Where(c => c.ContentDelta is not null).Select(c => c.ContentDelta!).ToList();
        messages.Should().ContainSingle(m => m.Contains("couldn't find", StringComparison.OrdinalIgnoreCase));
        // FR-056/research.md D19 — names the cause only; never pads with what didn't happen as a result.
        messages.Should().NotContain(m => m.Contains("focus", StringComparison.OrdinalIgnoreCase) || m.Contains("boundary", StringComparison.OrdinalIgnoreCase));

        record.Should().HaveCount(3);
        record[0].Attempted.Should().BeTrue();
        record[0].Succeeded.Should().BeFalse();
        record[1].Attempted.Should().BeFalse("step 2 was never reached");
        record[2].Attempted.Should().BeFalse("step 3 was never reached");
    }

    [Fact]
    public async Task RunAsync_ShouldStop_WhenStep3Fails_WithSteps1And2StandingAsSucceeded()
    {
        SucceedLocation();
        _boundaryService.ResolveAsync(Arg.Any<ConfirmedLocationData>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new BoundaryResolutionOutcome(BoundaryResolutionOutcomeType.Unavailable, null, "I couldn't work out the site boundary."));
        var record = new List<FlowStepResult>();

        var chunks = await RunAsync(Context(), throughStepIndex: 2, record);

        record.Should().HaveCount(3);
        record[0].Succeeded.Should().BeTrue();
        record[1].Succeeded.Should().BeTrue();
        record[2].Attempted.Should().BeTrue();
        record[2].Succeeded.Should().BeFalse();

        var messages = chunks.Where(c => c.ContentDelta is not null).Select(c => c.ContentDelta!).ToList();
        // Still the full N+1 = 4: steps 1 and 2 succeeded and were announced/reported normally;
        // only step 3's own failure message stands alone rather than pairing with a next
        // announcement — there is no step 4 to name regardless of the failure.
        messages.Should().HaveCount(4);
        messages[^1].Should().Contain("couldn't work out the site boundary");
        messages[^1].Should().NotContain("Now ", "FR-056/research.md D19 — the final message names only the cause, never a next step that will not run");
    }

    [Fact]
    public async Task RunAsync_ShouldSkipStep2_WhenTheViewerAlreadyFramesTheResolvedPlace()
    {
        SucceedLocation(name: "Al Safa Park 2");
        SucceedBoundary();
        var alreadyActive = new AskLucy.Domain.Chats.ActiveSiteLocation(25.15, 55.22, "Al Safa Park 2", 0.9);
        var record = new List<FlowStepResult>();

        var chunks = await RunAsync(Context(location: alreadyActive), throughStepIndex: 2, record);

        record[1].Skipped.Should().BeTrue();
        record[1].Attempted.Should().BeFalse();
        var messages = chunks.Where(c => c.ContentDelta is not null).Select(c => c.ContentDelta!).ToList();
        messages.Should().ContainSingle(m => m.Contains("already focused", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_ShouldSkipStep3_WhenTheSiteIsAlreadyOutlinedForThatPlace()
    {
        SucceedLocation(name: "Al Safa Park 2");
        var alreadyOutlined = new ActiveSiteBoundary(
            "Al Safa Park 2", 25.15, 55.22, [], 1000, 0.9, BoundaryConfidenceLevel.High, SiteBoundarySource.OsmBoundary, "OpenStreetMap");
        var record = new List<FlowStepResult>();

        var chunks = await RunAsync(Context(boundary: alreadyOutlined), throughStepIndex: 2, record);

        record[2].Skipped.Should().BeTrue();
        var messages = chunks.Where(c => c.ContentDelta is not null).Select(c => c.ContentDelta!).ToList();
        messages.Should().ContainSingle(m => m.Contains("already outlined", StringComparison.OrdinalIgnoreCase));
        await _boundaryService.DidNotReceive().ResolveAsync(Arg.Any<ConfirmedLocationData>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ShouldRunOnlyStep1_WhenScopedToIt()
    {
        SucceedLocation();
        var record = new List<FlowStepResult>();

        var chunks = await RunAsync(Context(), throughStepIndex: 0, record);

        record.Should().HaveCount(1);
        var messages = chunks.Where(c => c.ContentDelta is not null).Select(c => c.ContentDelta!).ToList();
        messages.Should().HaveCount(2, "a 1-step scoped run still produces announce + report");
        await _boundaryService.DidNotReceive().ResolveAsync(Arg.Any<ConfirmedLocationData>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ShouldStopAtBudget_WhenTheTurnHasAlreadyRunLong()
    {
        SucceedLocation();
        SucceedBoundary();
        var runtimeOptions = Microsoft.Extensions.Options.Options.Create(new ConversationRuntimeOptions { MaxTurnDurationSeconds = 5 });
        var capabilityCatalog = new ConversationCapabilityCatalog(
            new AgentToolCatalog(
                [new ResolveLocationCapability(_locationService), new AdjustViewerFocusCapability(), new ResolveSiteBoundaryCapability(_boundaryService)],
                new EmptyMcpToolRegistry()),
            new CapabilityIndexRetriever(Substitute.For<IEmbeddingService>(), runtimeOptions, NullLogger<CapabilityIndexRetriever>.Instance),
            runtimeOptions);
        var capabilityExecutor = new CapabilityExecutor(
            new AgentPolicyEvaluator(Substitute.For<IAgentPolicyRepository>()), new PermissiveSchemaValidator(),
            runtimeOptions, NullLogger<CapabilityExecutor>.Instance);
        var narrator = new CapabilityNarrator(NullLogger<CapabilityNarrator>.Instance);
        var runner = new FlowRunner(capabilityCatalog, capabilityExecutor, narrator, runtimeOptions, NullLogger<FlowRunner>.Instance);

        // Narration itself sleeps past the tiny budget, so step 2 onward finds it already exceeded.
        _provider.ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<string>(), Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await Task.Delay(TimeSpan.FromSeconds(6), call.Arg<CancellationToken>());
                return new ChatCompletionResult("done.", new ChatUsage(null, null, null, null, null));
            });

        var record = new List<FlowStepResult>();
        var chunks = new List<ChatStreamChunk>();
        await foreach (var chunk in runner.RunAsync(Request(), _flow, 2, Context(), """{"query":"Al Safa Park 2"}""", record, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        var messages = chunks.Where(c => c.ContentDelta is not null).Select(c => c.ContentDelta!).ToList();
        messages.Should().Contain(m => m.Contains("taking longer than expected", StringComparison.OrdinalIgnoreCase));
        record.Should().Contain(r => r.Reason != null && r.Reason.Contains("time budget", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NoFlowStep_ShouldBeIndependentlyOfferable()
    {
        // FR-060 — offering "outline the boundary" as a standalone row would let someone pick a
        // step that cannot run without step 1; only the flow's variants are offerable.
        var context = TurnContext.Empty("user-1", Guid.NewGuid());
        var outcome = TurnOutcome.None;

        new ResolveLocationCapability(_locationService).IsOfferable(context, outcome).Should().BeFalse();
        new AdjustViewerFocusCapability().IsOfferable(context, outcome).Should().BeFalse();
        new ResolveSiteBoundaryCapability(_boundaryService).IsOfferable(context, outcome).Should().BeFalse();
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
