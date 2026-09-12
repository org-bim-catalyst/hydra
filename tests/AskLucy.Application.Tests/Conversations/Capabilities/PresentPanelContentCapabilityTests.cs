using System.Text.Json;
using System.Text.Json.Nodes;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Runtime;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Runtime;
using AskLucy.Application.Mcp.Validation;
using AskLucy.Application.Options;
using AskLucy.Application.Panels;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Capabilities;

/// <summary>
/// specs/049 T019 — <see cref="PresentPanelContentCapability"/> pushes a valid composition and
/// refuses an invalid one via its declared <see cref="IAgentTool.InputSchemaJson"/>, exercised
/// against the real <see cref="JsonSchemaValidator"/> (not a substitute) so these tests prove the
/// schema itself works, not just that something was called.
/// </summary>
public sealed class PresentPanelContentCapabilityTests
{
    private readonly IPanelNotifier _panelNotifier = Substitute.For<IPanelNotifier>();
    private readonly JsonSchemaValidator _schemaValidator = new();

    private PresentPanelContentCapability BuildCapability() => new(_panelNotifier);

    private static AgentToolExecutionContext Context() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "user-1", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    private IReadOnlyList<string> ValidateAgainstSchema(PresentPanelContentCapability capability, string instanceJson)
    {
        using var schemaDocument = JsonDocument.Parse(capability.InputSchemaJson);
        using var instanceDocument = JsonDocument.Parse(instanceJson);
        return _schemaValidator.Validate(schemaDocument.RootElement, instanceDocument.RootElement, 32 * 1024);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPushAContentPanelRequest_WhenTitleAndContentAreProvided()
    {
        var input = JsonDocument.Parse(
            """{"title":"Al Safa Park 2","content":{"version":1,"blocks":[{"kind":"heading","text":"Al Safa Park 2"}]}}""");

        var result = await BuildCapability().ExecuteAsync(Context(), input, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        await _panelNotifier.Received(1).PanelRequestedAsync(
            "user-1",
            Arg.Is<PanelRequestDto>(r => r!.Kind == "content" && r.Content != null && r.TypeKey == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFail_WhenTitleIsMissing()
    {
        var input = JsonDocument.Parse("""{"content":{"version":1,"blocks":[{"kind":"heading","text":"x"}]}}""");

        var result = await BuildCapability().ExecuteAsync(Context(), input, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        await _panelNotifier.DidNotReceive().PanelRequestedAsync(
            Arg.Any<string>(), Arg.Any<PanelRequestDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFail_WhenContentIsMissing()
    {
        var input = JsonDocument.Parse("""{"title":"Al Safa Park 2"}""");

        var result = await BuildCapability().ExecuteAsync(Context(), input, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void InputSchemaJson_ShouldAccept_AWellFormedContentDocument()
    {
        var errors = ValidateAgainstSchema(BuildCapability(),
            """{"title":"Al Safa Park 2","content":{"version":1,"blocks":[{"kind":"heading","text":"x"},{"kind":"table","columns":["A"],"rows":[]}]}}""");

        errors.Should().BeEmpty();
    }

    [Fact]
    public void InputSchemaJson_ShouldReject_AnEmptyBlocksArray()
    {
        // FR-030 — content with no renderable blocks must never reach a push. Enforced entirely
        // by the schema's minItems:1 on `blocks`, via the same gate every capability's arguments
        // pass through (CapabilityExecutor), so no bespoke emptiness check exists in the
        // capability itself (specs/049 research D11).
        var errors = ValidateAgainstSchema(BuildCapability(),
            """{"title":"Al Safa Park 2","content":{"version":1,"blocks":[]}}""");

        errors.Should().NotBeEmpty();
    }

    [Fact]
    public void InputSchemaJson_ShouldReject_ABlockWithNoKind()
    {
        var errors = ValidateAgainstSchema(BuildCapability(),
            """{"title":"Al Safa Park 2","content":{"version":1,"blocks":[{"text":"no kind here"}]}}""");

        errors.Should().NotBeEmpty();
    }

    [Fact]
    public void InputSchemaJson_ShouldReject_MoreThanFiftyBlocks()
    {
        var blocks = string.Join(",", Enumerable.Range(0, 51).Select(_ => """{"kind":"divider"}"""));
        var instance = """{"title":"x","content":{"version":1,"blocks":[""" + blocks + """]}}""";
        var errors = ValidateAgainstSchema(BuildCapability(), instance);

        errors.Should().NotBeEmpty();
    }

    [Fact]
    public void InputSchemaJson_ShouldReject_AWrongVocabularyVersion()
    {
        var errors = ValidateAgainstSchema(BuildCapability(),
            """{"title":"x","content":{"version":2,"blocks":[{"kind":"divider"}]}}""");

        errors.Should().NotBeEmpty();
    }

    /// <summary>
    /// The backend-side half of specs/049 research D2's drift guard: keeps this capability's
    /// hand-written <see cref="IAgentTool.InputSchemaJson"/> literal in step with the same
    /// generated artifact <c>viewer/panels/content/blocks.schema.test.ts</c> guards on the
    /// frontend. Both tests exist because the vocabulary is genuinely defined twice (once as zod,
    /// once as this C# literal) and only generation-plus-comparison converts that duplication's
    /// drift into a failing build rather than a silent divergence (plan.md Complexity Tracking).
    /// </summary>
    [Fact]
    public void InputSchemaJson_ContentSubschema_ShouldMatchTheCommittedVocabularyArtifact()
    {
        using var capabilitySchema = JsonDocument.Parse(new PresentPanelContentCapability(_panelNotifier).InputSchemaJson);
        var embeddedContentSchema = capabilitySchema.RootElement.GetProperty("properties").GetProperty("content");

        var committedPath = FindCommittedSchemaPath();
        var committedNode = JsonNode.Parse(File.ReadAllText(committedPath))!.AsObject();
        // The generated artifact carries a top-level `$schema` draft marker that has no
        // counterpart in this hand-written literal's embedded "content" property — everything
        // else must match exactly.
        committedNode.Remove("$schema");

        JsonNode.DeepEquals(JsonNode.Parse(embeddedContentSchema.GetRawText()), committedNode).Should().BeTrue(
            "the capability's embedded schema and specs/049-panel-content-model/contracts/panel-content.schema.json " +
            "must be regenerated together — see viewer/panels/content/blocks.schema.test.ts for the frontend half " +
            "of this same guard");
    }

    /// <summary>
    /// specs/049 T053/T057 — the FR-030 "no empty panel" rule end to end, through the real gate a
    /// conversational capability actually passes through (<see cref="CapabilityExecutor"/>), not
    /// just the schema validator in isolation. No bespoke emptiness check exists in
    /// <see cref="PresentPanelContentCapability"/> itself — the schema's <c>minItems:1</c> on
    /// <c>blocks</c> is the entire enforcement (research D11), so this test is what proves that is
    /// actually true rather than merely asserted in a comment.
    /// </summary>
    [Fact]
    public async Task CapabilityExecutor_ShouldRefuseBeforeAnyPush_WhenContentHasNoBlocks()
    {
        var policies = Substitute.For<IAgentPolicyRepository>();
        policies.ListEnabledByToolNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
        var executor = new CapabilityExecutor(
            new AgentPolicyEvaluator(policies),
            _schemaValidator,
            Microsoft.Extensions.Options.Options.Create(new ConversationRuntimeOptions()),
            NullLogger<CapabilityExecutor>.Instance);
        var turnContext = TurnContext.Empty("user-1", Guid.NewGuid());

        var result = await executor.ExecuteAsync(
            BuildCapability(), turnContext, """{"title":"x","content":{"version":1,"blocks":[]}}""", CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        await _panelNotifier.DidNotReceive().PanelRequestedAsync(
            Arg.Any<string>(), Arg.Any<PanelRequestDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CapabilityExecutor_ShouldPushThroughToTheCapability_WhenContentIsWellFormed()
    {
        var policies = Substitute.For<IAgentPolicyRepository>();
        policies.ListEnabledByToolNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
        var executor = new CapabilityExecutor(
            new AgentPolicyEvaluator(policies),
            _schemaValidator,
            Microsoft.Extensions.Options.Options.Create(new ConversationRuntimeOptions()),
            NullLogger<CapabilityExecutor>.Instance);
        var turnContext = TurnContext.Empty("user-1", Guid.NewGuid());

        var result = await executor.ExecuteAsync(
            BuildCapability(), turnContext,
            """{"title":"Al Safa Park 2","content":{"version":1,"blocks":[{"kind":"heading","text":"x"}]}}""",
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        await _panelNotifier.Received(1).PanelRequestedAsync(
            "user-1", Arg.Any<PanelRequestDto>(), Arg.Any<CancellationToken>());
    }

    private static string FindCommittedSchemaPath()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(dir, "specs", "049-panel-content-model", "contracts", "panel-content.schema.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = Path.GetFullPath(Path.Combine(dir, ".."));
        }
        throw new FileNotFoundException("Could not locate the committed panel-content.schema.json by walking up from the test output directory.");
    }
}
