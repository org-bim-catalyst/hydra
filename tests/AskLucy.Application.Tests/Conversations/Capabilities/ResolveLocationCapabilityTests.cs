using System.Text.Json;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Runtime;
using AskLucy.Application.Locations;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Capabilities;

/// <summary>
/// 2026-09-11 live-testing regression: the viewer used to zoom tightly to a confirmed location's
/// real extent (specs/038-viewer-poi-zoom's <c>LocationType</c>/<c>Viewport</c>), but this
/// capability's JSON output silently dropped both fields, so the viewer always fell back to a
/// fixed, far more zoomed-out default. <see cref="ILocationResolutionService"/> itself never lost
/// this data (<c>LocationResolutionService.cs</c> still populates it) — it was lost only in the
/// capability boundary and <see cref="StructuredPayloadExtractor"/>'s reconstruction of it, which
/// is exactly what these two tests guard together.
/// </summary>
public sealed class ResolveLocationCapabilityTests
{
    private readonly ILocationResolutionService _locationService = Substitute.For<ILocationResolutionService>();

    private ResolveLocationCapability BuildCapability() => new(_locationService);

    private static AgentToolExecutionContext Context() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "user-1", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    [Fact]
    public async Task ExecuteAsync_ShouldIncludeLocationTypeAndViewport_WhenTheGeocoderReturnsThem()
    {
        var viewport = new ViewportBounds(25.16, 55.23, 25.15, 55.21);
        _locationService.ResolveQueryAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new LocationResolutionOutcome(
                LocationResolutionOutcomeType.Confirmed,
                new ConfirmedLocationData(25.156, 55.2218, "Al Safa Park 2", 0.9, LocationType: "GEOMETRIC_CENTER", Viewport: viewport),
                "confirmed"));

        var result = await BuildCapability().ExecuteAsync(Context(), JsonDocument.Parse("""{"query":"Al Safa Park 2"}"""), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        var root = result.Output!.RootElement;
        root.GetProperty("locationType").GetString().Should().Be("GEOMETRIC_CENTER");
        var viewportEl = root.GetProperty("viewport");
        viewportEl.GetProperty("northeastLat").GetDouble().Should().Be(25.16);
        viewportEl.GetProperty("southwestLng").GetDouble().Should().Be(55.21);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldOmitViewport_WhenTheGeocoderReturnsNone()
    {
        _locationService.ResolveQueryAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new LocationResolutionOutcome(
                LocationResolutionOutcomeType.Confirmed,
                new ConfirmedLocationData(25.156, 55.2218, "Al Safa Park 2", 0.9),
                "confirmed"));

        var result = await BuildCapability().ExecuteAsync(Context(), JsonDocument.Parse("""{"query":"Al Safa Park 2"}"""), CancellationToken.None);

        result.Output!.RootElement.GetProperty("viewport").ValueKind.Should().Be(JsonValueKind.Null);
        result.Output!.RootElement.GetProperty("locationType").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void StructuredPayloadExtractor_ShouldReconstructLocationTypeAndViewport_FromTheCapabilitysOwnJson()
    {
        const string json = """
            {"outcome":"Confirmed","locationName":"Al Safa Park 2","latitude":25.156,"longitude":55.2218,"confidence":0.9,
             "locationType":"GEOMETRIC_CENTER","viewport":{"northeastLat":25.16,"northeastLng":55.23,"southwestLat":25.15,"southwestLng":55.21}}
            """;

        var chunk = StructuredPayloadExtractor.TryExtract(ResolveLocationCapability.CapabilityKey, json);

        chunk.Should().NotBeNull();
        chunk!.ConfirmedLocation.Should().NotBeNull();
        chunk.ConfirmedLocation!.LocationType.Should().Be("GEOMETRIC_CENTER");
        chunk.ConfirmedLocation.Viewport.Should().Be(new ViewportBounds(25.16, 55.23, 25.15, 55.21));
    }
}
