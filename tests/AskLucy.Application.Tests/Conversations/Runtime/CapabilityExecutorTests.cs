using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Runtime;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Runtime;
using AskLucy.Domain.Agents;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Runtime;

/// <summary>
/// specs/045 — the gates a capability passes when a conversation, rather than the background
/// agent runtime, is what reached it (FR-015).
///
/// <para>
/// The point of these tests is that <b>nothing is relaxed for chat</b>. Schema validation,
/// permissions and the risk/approval gate all still apply, and they live in the executor rather
/// than in each capability so that a newly written capability cannot skip them by omission.
/// </para>
/// </summary>
public sealed class CapabilityExecutorTests
{
    private readonly IAgentPolicyRepository _policies = Substitute.For<IAgentPolicyRepository>();
    private readonly IJsonSchemaValidator _schemaValidator = Substitute.For<IJsonSchemaValidator>();
    private readonly CapabilityExecutor _executor;

    private static readonly string[] SchemaValidationFailure = ["query is required"];

    public CapabilityExecutorTests()
    {
        _schemaValidator.Validate(Arg.Any<JsonElement>(), Arg.Any<JsonElement>(), Arg.Any<long>())
            .Returns(Array.Empty<string>());
        _policies.ListEnabledByToolNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<AgentPolicy>());

        _executor = new CapabilityExecutor(
            new AgentPolicyEvaluator(_policies),
            _schemaValidator,
            NullLogger<CapabilityExecutor>.Instance);
    }

    private static TurnContext Context(params AgentToolPermission[] granted) =>
        TurnContext.Empty("user-1", Guid.NewGuid()) with
        {
            GrantedPermissions = new HashSet<AgentToolPermission>(granted),
        };

    [Fact]
    public async Task ExecuteAsync_ShouldRunTheCapability_WhenEveryGatePasses()
    {
        var capability = new StubCapability();

        var result = await _executor.ExecuteAsync(capability, Context(), "{\"ok\":true}", CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.CapabilityKey.Should().Be("stub");
        result.ResultJson.Should().Contain("done");
        capability.WasInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRefuse_WhenArgumentsFailTheSchema()
    {
        // The grounding check the deciding model cannot see or shape itself around: the schema is
        // Tier 3 and never reaches a prompt.
        _schemaValidator.Validate(Arg.Any<JsonElement>(), Arg.Any<JsonElement>(), Arg.Any<long>())
            .Returns(SchemaValidationFailure);
        var capability = new StubCapability();

        var result = await _executor.ExecuteAsync(capability, Context(), "{}", CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ResultJson.Should().Contain("query is required");
        capability.WasInvoked.Should().BeFalse("a capability must never run on arguments that failed validation");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRefuse_WhenArgumentsAreNotValidJson()
    {
        var capability = new StubCapability();

        var result = await _executor.ExecuteAsync(capability, Context(), "not json", CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ResultJson.Should().Contain("not valid JSON");
        capability.WasInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRefuse_WhenAPermissionIsMissing()
    {
        var capability = new StubCapability { Permissions = [AgentToolPermission.ReadKnowledge] };

        var result = await _executor.ExecuteAsync(capability, Context(), "{}", CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ResultJson.Should().Contain("permission").And.Contain("ReadKnowledge");
        capability.WasInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRun_WhenTheMissingPermissionIsActuallyGranted()
    {
        var capability = new StubCapability { Permissions = [AgentToolPermission.ReadKnowledge] };

        var result = await _executor.ExecuteAsync(
            capability, Context(AgentToolPermission.ReadKnowledge), "{}", CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        capability.WasInvoked.Should().BeTrue();
    }

    [Theory]
    [InlineData(AgentToolRiskLevel.High)]
    [InlineData(AgentToolRiskLevel.Critical)]
    public async Task ExecuteAsync_ShouldRefuseARiskyCapability_WithNoMatchingPolicy(AgentToolRiskLevel risk)
    {
        // A streaming turn has nowhere to suspend for out-of-band approval, so it stops before
        // running rather than running and reporting afterwards.
        var capability = new StubCapability { Risk = risk };

        var result = await _executor.ExecuteAsync(capability, Context(), "{}", CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ResultJson.Should().Contain("approval");
        capability.WasInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldConvertAThrow_IntoAReportableFailure()
    {
        // Isolation, not suppression (constitution §2.VIII). Every capability documents a
        // never-throws contract; turn integrity must not depend on one of them keeping it.
        var capability = new StubCapability { ThrowOnExecute = new InvalidOperationException("upstream exploded") };

        var result = await _executor.ExecuteAsync(capability, Context(), "{}", CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ResultJson.Should().Contain("upstream exploded");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPropagateCallerCancellation_RatherThanRecordingItAsAFailure()
    {
        // A user navigating away is a user action, and reporting it as "the capability failed"
        // would put a wrong explanation in the turn record and in front of the next reader.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var capability = new StubCapability { ThrowOnExecute = new OperationCanceledException() };

        var act = async () => await _executor.ExecuteAsync(capability, Context(), "{}", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldCarryTheCapabilitysOwnFailureReason_Through()
    {
        // The narration step reports whatever comes back, so a failure reason has to survive
        // intact rather than being flattened into a generic message.
        var capability = new StubCapability { FailWith = "I couldn't find a place matching that name." };

        var result = await _executor.ExecuteAsync(capability, Context(), "{}", CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ResultJson.Should().Be("I couldn't find a place matching that name.");
    }

    /// <summary>A capability whose behaviour each test dials in — no substitute, because the gates under test read many members.</summary>
    private sealed class StubCapability : IConversationCapability
    {
        public bool WasInvoked { get; private set; }

        public Exception? ThrowOnExecute { get; init; }

        public string? FailWith { get; init; }

        public AgentToolRiskLevel Risk { get; init; } = AgentToolRiskLevel.Low;

        public IReadOnlyList<AgentToolPermission> Permissions { get; init; } = [];

        public string Name => "stub";

        public string Description => "A stub capability.";

        public string WhenToUse => "Use when a test needs a capability to exist.";

        public string ArgumentHint => "anything";

        public string UsageGuidance => "Report what came back.";

        public string Label => "Stub";

        public string OfferDescription => "A stub.";

        public string AcknowledgementTemplate => "Running the stub.";

        public AgentToolRiskLevel RiskLevel => Risk;

        public IReadOnlyList<AgentToolPermission> RequiredPermissions => Permissions;

        public string InputSchemaJson => """{"type":"object"}""";

        public string OutputSchemaJson => """{"type":"object"}""";

        public CapabilityDuration ExpectedDuration => CapabilityDuration.Brief;

        public bool IsAvailable(TurnContext context) => true;

        public Task<AgentToolResult> ExecuteAsync(
            AgentToolExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
        {
            WasInvoked = true;

            if (ThrowOnExecute is not null)
            {
                throw ThrowOnExecute;
            }

            return Task.FromResult(FailWith is not null
                ? AgentToolResult.Failure(FailWith)
                : AgentToolResult.Success(JsonSerializer.SerializeToDocument(new { status = "done" })));
        }
    }
}
