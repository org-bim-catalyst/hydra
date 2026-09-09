using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Runtime;
using AskLucy.Domain.Agents;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Runtime;

/// <summary>
/// specs/045 US2 (T066) — the five conditions under which the offer step must not run at all
/// (FR-025a). <see cref="OfferSuppressionRules.Evaluate"/> is pure and synchronous with nothing to
/// call out to, so a suppressed verdict here is itself the "no model call" guarantee: there is no
/// path through this method that could have made one.
/// </summary>
public sealed class OfferSuppressionTests
{
    private readonly AgentToolCatalog _toolCatalog = new([new StubCapability()], new EmptyMcpToolRegistry());
    private readonly ConversationCapabilityCatalog _catalog;

    public OfferSuppressionTests()
    {
        var runtimeOptions = Microsoft.Extensions.Options.Options.Create(new Application.Options.ConversationRuntimeOptions());
        var indexRetriever = new CapabilityIndexRetriever(
            Substitute.For<IEmbeddingService>(), runtimeOptions, Microsoft.Extensions.Logging.Abstractions.NullLogger<CapabilityIndexRetriever>.Instance);
        _catalog = new ConversationCapabilityCatalog(_toolCatalog, indexRetriever, runtimeOptions);
    }

    private static TurnContext Context() => TurnContext.Empty("user-1", Guid.NewGuid());

    [Fact]
    public void Evaluate_ShouldSuppress_WhenSuggestedActionsAreDisabled()
    {
        var reason = OfferSuppressionRules.Evaluate(TurnIntent.Suggest, Context(), TurnOutcome.None, _catalog, suggestedActionsEnabled: false);

        reason.Should().Be(OfferSuppressionReason.SuggestedActionsDisabled);
    }

    [Fact]
    public void Evaluate_ShouldSuppress_WhenTheTurnOnlyAnswered()
    {
        // FR-025a.1 — answering a question is not a reason to ask what to do next.
        var reason = OfferSuppressionRules.Evaluate(TurnIntent.Answer, Context(), TurnOutcome.None, _catalog, suggestedActionsEnabled: true);

        reason.Should().Be(OfferSuppressionReason.AnsweredOnly);
    }

    [Fact]
    public void Evaluate_ShouldNotSuppress_ForASuggestIntentEvenThoughItTookNoBeats()
    {
        // The distinction the offer step exists for (research.md D18): TurnIntent.Suggest takes
        // the same words-only mechanics as Answer, but it is exactly the "do you know X?" case
        // that should still be offered something.
        var reason = OfferSuppressionRules.Evaluate(TurnIntent.Suggest, Context(), TurnOutcome.None, _catalog, suggestedActionsEnabled: true);

        reason.Should().Be(OfferSuppressionReason.None);
    }

    [Fact]
    public void Evaluate_ShouldSuppress_WhenTheUserDeclinedTheLastOffer()
    {
        // FR-025a.3 — a decline is an answer, and re-asking is nagging.
        var outcome = new TurnOutcome([], false, [], UserDeclinedLastOffer: true);

        var reason = OfferSuppressionRules.Evaluate(TurnIntent.Suggest, Context(), outcome, _catalog, suggestedActionsEnabled: true);

        reason.Should().Be(OfferSuppressionReason.UserDeclinedLastOffer);
    }

    [Fact]
    public void Evaluate_ShouldSuppress_WhenNothingIsOfferable()
    {
        // FR-025a.2 — the only registered capability was just invoked this turn, so
        // ConversationCapabilityCatalog.OfferableFor excludes it and nothing is left.
        var outcome = new TurnOutcome(["stub"], false, [], UserDeclinedLastOffer: false);

        var reason = OfferSuppressionRules.Evaluate(TurnIntent.Act, Context(), outcome, _catalog, suggestedActionsEnabled: true);

        reason.Should().Be(OfferSuppressionReason.NothingOfferable);
    }

    [Fact]
    public void Evaluate_ShouldSuppress_WhenEverythingOfferableWasAlreadyOfferedAndIgnored()
    {
        // FR-025a.4 — folded into OfferableFor's own ignored-key filter, so it collapses onto the
        // same NothingOfferable verdict rather than needing a separate code path.
        var outcome = new TurnOutcome([], false, ["stub"], UserDeclinedLastOffer: false);

        var reason = OfferSuppressionRules.Evaluate(TurnIntent.Act, Context(), outcome, _catalog, suggestedActionsEnabled: true);

        reason.Should().Be(OfferSuppressionReason.NothingOfferable);
    }

    [Fact]
    public void Evaluate_ShouldNotSuppress_WhenACapabilityIsGenuinelyOfferable()
    {
        var reason = OfferSuppressionRules.Evaluate(TurnIntent.Act, Context(), TurnOutcome.None, _catalog, suggestedActionsEnabled: true);

        reason.Should().Be(OfferSuppressionReason.None);
    }

    /// <summary>Available and offerable by default (<see cref="IsAvailable"/> only; <see cref="IConversationCapability.IsOfferable"/> defaults to it).</summary>
    private sealed class StubCapability : IConversationCapability
    {
        public string Name => "stub";

        public string Description => "A stub capability.";

        public string WhenToUse => "Use when a test needs a capability to exist.";

        public string ArgumentHint => "nothing";

        public string UsageGuidance => "Report what came back.";

        public string Label => "Stub";

        public string OfferDescription => "Runs the stub.";

        public string AcknowledgementTemplate => "Running the stub.";

        public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Low;

        public IReadOnlyList<AgentToolPermission> RequiredPermissions => [];

        public string InputSchemaJson => """{"type":"object"}""";

        public string OutputSchemaJson => """{"type":"object"}""";

        public CapabilityDuration ExpectedDuration => CapabilityDuration.Brief;

        public SubAgentArea Area => SubAgentArea.Location;

        public bool IsAvailable(TurnContext context) => true;

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
