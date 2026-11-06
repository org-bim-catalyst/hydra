using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Runtime;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Spatial;

/// <summary>
/// specs/094 — the capability's output becomes the one viewer command, and a question or a
/// malformed payload never does.
/// </summary>
public sealed class SpatialLayersExtractionTests
{
    private const string ResultWithLayers =
        """
        {"analysisId":"a1","status":"Complete","mode":"replace","count":1,
         "layers":[{"layerId":"op-1:school","sourceOperation":"op-1","name":"Schools in Muscat",
         "legendLabel":"Schools","kind":"markers","visible":true,
         "markers":[{"id":"osm_1","name":"A","latitude":23.5,"longitude":58.3}],
         "style":{"color":"#4285F4","markerShape":"simple-dot"}}],
         "warnings":[],"coverage":[]}
        """;

    [Fact]
    public void TryExtract_ShouldMapTheCapabilityOutputToTheViewerCommand()
    {
        var chunk = StructuredPayloadExtractor.TryExtract(SpatialAnalysisCapability.CapabilityKey, ResultWithLayers);

        chunk.Should().NotBeNull();
        chunk!.SpatialLayers.Should().NotBeNull();
        chunk.SpatialLayers!.AnalysisId.Should().Be("a1");
        chunk.SpatialLayers.Layers.Should().ContainSingle();
        chunk.SpatialLayers.Layers[0].Markers.Should().ContainSingle();
    }

    [Fact]
    public void TryExtract_ShouldNotEmitACommandForAQuestion()
    {
        const string question =
            """
            {"analysisId":"","status":"question","mode":"ask","awaitingChoice":true,
             "question":"Clear or keep?","layers":[],"warnings":[],"coverage":[]}
            """;

        StructuredPayloadExtractor.TryExtract(SpatialAnalysisCapability.CapabilityKey, question)
            .Should().BeNull();
    }

    [Fact]
    public void TryExtract_ShouldNotThrowOnMalformedOutput()
    {
        var chunk = StructuredPayloadExtractor.TryExtract(
            SpatialAnalysisCapability.CapabilityKey, "{not valid json");

        chunk.Should().BeNull();
    }
}
