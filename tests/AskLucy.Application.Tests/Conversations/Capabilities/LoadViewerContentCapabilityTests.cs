using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Runtime;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Runtime;
using AskLucy.Application.Mcp.Validation;
using AskLucy.Application.Options;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Capabilities;

/// <summary>
/// specs/051-viewer-scene-content-api T034 — mirrors
/// <see cref="PresentPanelContentCapabilityTests"/>'s structure. Unlike
/// <see cref="PresentPanelContentCapability"/>, this capability has no notifier dependency: it
/// only validates and echoes its input back as the tool result — the actual client-visible push
/// happens generically via <c>StructuredPayloadExtractor</c>/<c>AiController</c>'s
/// <c>__VIEWER_CONTENT__</c> event, exercised separately from this capability itself (research D8).
/// </summary>
public sealed class LoadViewerContentCapabilityTests
{
    private readonly JsonSchemaValidator _schemaValidator = new();

    private static LoadViewerContentCapability BuildCapability() => new();

    private static AgentToolExecutionContext Context() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "user-1", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    private IReadOnlyList<string> ValidateAgainstSchema(LoadViewerContentCapability capability, string instanceJson)
    {
        using var schemaDocument = JsonDocument.Parse(capability.InputSchemaJson);
        using var instanceDocument = JsonDocument.Parse(instanceJson);
        return _schemaValidator.Validate(schemaDocument.RootElement, instanceDocument.RootElement, 32 * 1024);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSucceed_WhenFileIdAndCoordinatesAreProvided()
    {
        var input = JsonDocument.Parse("""{"fileId":"file-1","latitude":25.2048,"longitude":55.2708}""");

        var result = await BuildCapability().ExecuteAsync(Context(), input, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldDefaultOptionalFields_WhenOmitted()
    {
        var input = JsonDocument.Parse("""{"fileId":"file-1","latitude":25.2048,"longitude":55.2708}""");

        var result = await BuildCapability().ExecuteAsync(Context(), input, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        var output = result.Output!.RootElement;
        output.GetProperty("fileId").GetString().Should().Be("file-1");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFail_WhenFileIdIsMissing()
    {
        var input = JsonDocument.Parse("""{"latitude":25.2048,"longitude":55.2708}""");

        var result = await BuildCapability().ExecuteAsync(Context(), input, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFail_WhenLatitudeIsOutOfRange()
    {
        var input = JsonDocument.Parse("""{"fileId":"file-1","latitude":999,"longitude":55.2708}""");

        var result = await BuildCapability().ExecuteAsync(Context(), input, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFail_WhenLongitudeIsOutOfRange()
    {
        var input = JsonDocument.Parse("""{"fileId":"file-1","latitude":25.2048,"longitude":999}""");

        var result = await BuildCapability().ExecuteAsync(Context(), input, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void InputSchemaJson_ShouldAccept_AWellFormedRequest()
    {
        var errors = ValidateAgainstSchema(BuildCapability(),
            """{"fileId":"file-1","latitude":25.2048,"longitude":55.2708,"heightMetres":10,"orientationDegrees":90,"scale":2}""");

        errors.Should().BeEmpty();
    }

    [Fact]
    public void InputSchemaJson_ShouldReject_MissingFileId()
    {
        var errors = ValidateAgainstSchema(BuildCapability(), """{"latitude":25.2048,"longitude":55.2708}""");

        errors.Should().NotBeEmpty();
    }

    [Fact]
    public void InputSchemaJson_ShouldReject_OutOfRangeLatitude()
    {
        var errors = ValidateAgainstSchema(BuildCapability(), """{"fileId":"file-1","latitude":999,"longitude":0}""");

        errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CapabilityExecutor_ShouldSucceed_WhenRequestIsWellFormed()
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
            BuildCapability(), turnContext, """{"fileId":"file-1","latitude":25.2048,"longitude":55.2708}""", CancellationToken.None);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void IsAvailable_ShouldAlwaysBeTrue()
    {
        BuildCapability().IsAvailable(TurnContext.Empty("user-1", Guid.NewGuid())).Should().BeTrue();
    }

    [Fact]
    public void IsOfferable_ShouldAlwaysBeFalse()
    {
        // Mirrors AdjustViewerFocusCapability's own reasoning — asked for directly, not offered.
        BuildCapability().IsOfferable(TurnContext.Empty("user-1", Guid.NewGuid()), null!).Should().BeFalse();
    }
}
