using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Runtime;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Runtime;

/// <summary>
/// A boundary lookup that could not draw an outline still moves the viewer: its failure carries the
/// same <c>direction</c> shape <c>adjust_viewer_focus</c> emits, so the client runs its existing
/// zoom command without any new handling. The two shapes must stay distinguishable — a real
/// boundary payload never carries a <c>direction</c>, and the zoom payload never carries a polygon.
/// </summary>
public sealed class StructuredPayloadExtractorBoundaryUnavailableTests
{
    [Fact]
    public void AnUnavailableBoundary_BecomesAZoomCommand()
    {
        var chunk = StructuredPayloadExtractor.TryExtract(
            ResolveSiteBoundaryCapability.CapabilityKey, """{"direction":"in"}""");

        chunk.Should().NotBeNull();
        chunk!.ViewerZoom.Should().NotBeNull();
        chunk.ViewerZoom!.Direction.Should().Be("in");
        chunk.ConfirmedBoundary.Should().BeNull("no outline was resolved, so none may be drawn");
    }

    [Fact]
    public void ADrawnBoundary_StillBecomesABoundaryCommandAndNotAZoom()
    {
        const string payload = """
            {"siteName":"Al Safa Park 2","centroidLatitude":25.156,"centroidLongitude":55.2218,
             "polygon":[{"latitude":25.155,"longitude":55.221},{"latitude":25.157,"longitude":55.221},
                        {"latitude":25.157,"longitude":55.223}],
             "areaSquareMeters":42000,"confidence":0.7,"confidenceLevel":"Medium",
             "sourceType":"OsmBoundary","source":"OpenStreetMap"}
            """;

        var chunk = StructuredPayloadExtractor.TryExtract(ResolveSiteBoundaryCapability.CapabilityKey, payload);

        chunk.Should().NotBeNull();
        chunk!.ConfirmedBoundary.Should().NotBeNull();
        chunk.ViewerZoom.Should().BeNull();
    }
}
