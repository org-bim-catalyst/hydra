using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Runtime;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Conversations;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Runtime;

/// <summary>
/// specs/045 US2 (T065) — <see cref="SuggestedActionGrounder"/> turns the offer step's raw JSON
/// into a grounded offer. Grounding differs by kind (FR-024): <c>capability</c> rows are checked
/// absolutely against the registry and schema; <c>followUp</c> rows have no key to check and are
/// validated only best-effort for doing-phrasing; a whole offer that ends up with nothing
/// substantive is dropped entirely rather than shown as a lone decline row.
/// </summary>
public sealed class SuggestedActionGrounderTests
{
    private readonly IJsonSchemaValidator _schemaValidator = Substitute.For<IJsonSchemaValidator>();
    private readonly SuggestedActionGrounder _grounder;
    private readonly ConversationCapabilityCatalog _catalog;
    private static readonly string[] ArgumentValidationFailure = ["query is required"];

    public SuggestedActionGrounderTests()
    {
        _schemaValidator.Validate(Arg.Any<JsonElement>(), Arg.Any<JsonElement>(), Arg.Any<long>())
            .Returns(Array.Empty<string>());
        _grounder = new SuggestedActionGrounder(_schemaValidator);

        var toolCatalog = new AgentToolCatalog([new StubCapability()], new EmptyMcpToolRegistry());
        var runtimeOptions = Microsoft.Extensions.Options.Options.Create(new Application.Options.ConversationRuntimeOptions());
        var indexRetriever = new CapabilityIndexRetriever(
            Substitute.For<IEmbeddingService>(), runtimeOptions, Microsoft.Extensions.Logging.Abstractions.NullLogger<CapabilityIndexRetriever>.Instance);
        _catalog = new ConversationCapabilityCatalog(toolCatalog, indexRetriever, runtimeOptions);
    }

    private static TurnContext Context() => TurnContext.Empty("user-1", Guid.NewGuid());

    [Fact]
    public void Ground_ShouldKeepAValidMixedOffer_Intact()
    {
        const string content = """
            {"question":"What would you like to do next?","actions":[
              {"kind":"capability","key":"stub","arguments":{"query":"x"},"label":"Run the stub","description":"Does the stub thing."},
              {"kind":"followUp","text":"Compare it with the last one you looked at","label":"Compare it","description":"Against the last site."}
            ]}
            """;

        var result = _grounder.Ground(content, Context(), _catalog, maxSuggestedActions: 5);

        result.Offer.Should().NotBeNull();
        result.Offer!.Question.Should().Be("What would you like to do next?");
        result.Offer.Actions.Should().HaveCount(3, "two substantive rows plus the server-appended decline");
        result.Offer.Actions[0].Kind.Should().Be(SuggestedActionKind.Capability);
        result.Offer.Actions[0].Key.Should().Be("stub");
        result.Offer.Actions[1].Kind.Should().Be(SuggestedActionKind.FollowUp);
        result.Offer.Actions[1].Text.Should().Be("Compare it with the last one you looked at");
        result.Offer.Actions[^1].IsDecline.Should().BeTrue("the decline row is always appended last");
    }

    [Fact]
    public void Ground_ShouldDiscardAndLog_AnUnregisteredCapabilityKey()
    {
        const string content = """{"actions":[{"kind":"capability","key":"does_not_exist","label":"Do it","description":"…"}]}""";

        var result = _grounder.Ground(content, Context(), _catalog, maxSuggestedActions: 5);

        result.Offer.Should().BeNull("nothing substantive survived");
        result.DroppedReasons.Should().ContainMatch("*does_not_exist*not an available capability*");
    }

    [Fact]
    public void Ground_ShouldDiscardAndLog_ArgumentsThatFailTheSchema()
    {
        _schemaValidator.Validate(Arg.Any<JsonElement>(), Arg.Any<JsonElement>(), Arg.Any<long>())
            .Returns(ArgumentValidationFailure);
        const string content = """{"actions":[{"kind":"capability","key":"stub","arguments":{},"label":"Do it","description":"…"}]}""";

        var result = _grounder.Ground(content, Context(), _catalog, maxSuggestedActions: 5);

        result.Offer.Should().BeNull();
        result.DroppedReasons.Should().ContainMatch("*stub*query is required*");
    }

    [Fact]
    public void Ground_ShouldDiscardAndLog_AFollowUpThatPromisesPlatformWork()
    {
        const string content = """
            {"actions":[
              {"kind":"followUp","text":"I'll search your documents for it","label":"Search","description":"…"},
              {"kind":"capability","key":"stub","arguments":{},"label":"Run the stub","description":"…"}
            ]}
            """;

        var result = _grounder.Ground(content, Context(), _catalog, maxSuggestedActions: 5);

        result.Offer.Should().NotBeNull("the capability row is still valid even though the follow-up was dropped");
        result.Offer!.Actions.Should().ContainSingle(a => a.Kind == SuggestedActionKind.Capability);
        result.DroppedReasons.Should().ContainMatch("*implied platform work*");
    }

    [Fact]
    public void Ground_ShouldDropTheWholeOffer_WhenNothingSubstantiveSurvives()
    {
        const string content = """
            {"actions":[
              {"kind":"capability","key":"does_not_exist","label":"Do it","description":"…"},
              {"kind":"followUp","text":"I'll open the panel for you","label":"Open it","description":"…"}
            ]}
            """;

        var result = _grounder.Ground(content, Context(), _catalog, maxSuggestedActions: 5);

        result.Offer.Should().BeNull("a decline row alone is never shown on its own");
        result.DroppedReasons.Should().HaveCount(2);
    }

    [Fact]
    public void Ground_ShouldReturnNoOffer_WhenTheModelProposesNoActions()
    {
        var result = _grounder.Ground("""{"question":"…","actions":[]}""", Context(), _catalog, maxSuggestedActions: 5);

        result.Offer.Should().BeNull("FR-025c: an empty actions array is a legitimate, common answer");
        result.DroppedReasons.Should().BeEmpty();
    }

    [Fact]
    public void Ground_ShouldReturnNoOffer_ForUnparseableContent()
    {
        var result = _grounder.Ground("not json at all", Context(), _catalog, maxSuggestedActions: 5);

        result.Offer.Should().BeNull();
        result.DroppedReasons.Should().ContainSingle();
    }

    [Fact]
    public void Ground_ShouldDropAFlowVariantRow_BecauseNoFlowRegistryExistsYet()
    {
        const string content = """{"actions":[{"kind":"flowVariant","key":"locate_a_place:full","label":"Focus and outline","description":"…"}]}""";

        var result = _grounder.Ground(content, Context(), _catalog, maxSuggestedActions: 5);

        result.Offer.Should().BeNull();
        result.DroppedReasons.Should().ContainMatch("*flow variant*");
    }

    [Fact]
    public void Ground_ShouldEnforceTheSubstantiveCap()
    {
        var actions = string.Join(",", Enumerable.Range(0, 5)
            .Select(i => $$"""{"kind":"followUp","text":"Suggestion number {{i}} worth reading","label":"Option {{i}}","description":"…"}"""));
        var content = $$"""{"actions":[{{actions}}]}""";

        var result = _grounder.Ground(content, Context(), _catalog, maxSuggestedActions: 3);

        result.Offer.Should().NotBeNull();
        result.Offer!.Actions.Should().HaveCount(3, "2 substantive rows (cap 3 minus the decline) plus the decline");
        result.DroppedReasons.Should().ContainMatch("*exceeded the cap*");
    }

    /// <summary>Mirrors <c>CapabilityExecutorTests</c>' own stub — no substitute, because grounding reads many members.</summary>
    private sealed class StubCapability : IConversationCapability
    {
        public string Name => "stub";

        public string Description => "A stub capability.";

        public string WhenToUse => "Use when a test needs a capability to exist.";

        public string ArgumentHint => "a query";

        public string UsageGuidance => "Report what came back.";

        public string Label => "Stub";

        public string OfferDescription => "Runs the stub.";

        public string AcknowledgementTemplate => "Running the stub.";

        public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Low;

        public IReadOnlyList<AgentToolPermission> RequiredPermissions => [];

        public string InputSchemaJson => """{"type":"object","properties":{"query":{"type":"string"}},"required":["query"]}""";

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
