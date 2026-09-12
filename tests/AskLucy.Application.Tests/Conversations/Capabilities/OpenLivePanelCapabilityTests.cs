using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Mcp.Validation;
using AskLucy.Application.Panels;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Capabilities;

/// <summary>
/// specs/049 T054 — <see cref="OpenLivePanelCapability"/> is honestly unavailable in this feature
/// (no live panel kind ships here, and the server has no way to learn what the client-side
/// registry currently holds — see the capability's own class remarks), and its request shape is
/// validated exactly like every other capability's.
/// </summary>
public sealed class OpenLivePanelCapabilityTests
{
    private readonly IPanelNotifier _panelNotifier = Substitute.For<IPanelNotifier>();
    private readonly JsonSchemaValidator _schemaValidator = new();

    private OpenLivePanelCapability BuildCapability() => new(_panelNotifier);

    private static AgentToolExecutionContext Context() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "user-1", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    [Fact]
    public void IsAvailable_ShouldAlwaysBeFalse_BecauseNoLiveKindShipsInThisFeature()
    {
        var context = TurnContext.Empty("user-1", Guid.NewGuid());

        BuildCapability().IsAvailable(context).Should().BeFalse();
    }

    [Fact]
    public void IsOfferable_ShouldAlwaysBeFalse()
    {
        var context = TurnContext.Empty("user-1", Guid.NewGuid());

        BuildCapability().IsOfferable(context, TurnOutcome.None).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPushALivePanelRequest_WhenTypeKeyTitleAndDataAreProvided()
    {
        var input = JsonDocument.Parse("""{"typeKey":"solar-controls","title":"Sun position","data":{"whatever":true}}""");

        var result = await BuildCapability().ExecuteAsync(Context(), input, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        await _panelNotifier.Received(1).PanelRequestedAsync(
            "user-1",
            Arg.Is<PanelRequestDto>(r => r!.Kind == "live" && r.TypeKey == "solar-controls" && r.Content == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFail_WhenTypeKeyIsMissing()
    {
        var input = JsonDocument.Parse("""{"title":"Sun position","data":{}}""");

        var result = await BuildCapability().ExecuteAsync(Context(), input, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        await _panelNotifier.DidNotReceive().PanelRequestedAsync(
            Arg.Any<string>(), Arg.Any<PanelRequestDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void InputSchemaJson_ShouldReject_AMissingTypeKey()
    {
        using var schemaDocument = JsonDocument.Parse(BuildCapability().InputSchemaJson);
        using var instanceDocument = JsonDocument.Parse("""{"title":"x","data":{}}""");

        var errors = _schemaValidator.Validate(schemaDocument.RootElement, instanceDocument.RootElement, 32 * 1024);

        errors.Should().NotBeEmpty();
    }

    [Fact]
    public void InputSchemaJson_ShouldAccept_AWellFormedRequest()
    {
        using var schemaDocument = JsonDocument.Parse(BuildCapability().InputSchemaJson);
        using var instanceDocument = JsonDocument.Parse("""{"typeKey":"solar-controls","title":"Sun position","data":{}}""");

        var errors = _schemaValidator.Validate(schemaDocument.RootElement, instanceDocument.RootElement, 32 * 1024);

        errors.Should().BeEmpty();
    }
}
