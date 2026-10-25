using System.Text.Json;
using AskLucy.Application.SiteAnalysis;
using AskLucy.Domain.SiteAnalysis;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.SiteAnalysis;

/// <summary>
/// specs/088-environmental-context-specialist T011 — the composer's multi-section entry point. It must
/// emit only the vocabulary the client already renders, keep the provenance block last (FR-011), and
/// preserve the envelope the shipped composition produces.
/// </summary>
public sealed class SiteAnalysisContentComposerTests
{
    private static readonly string[] AllowedKinds = ["heading", "text", "keyValue", "divider"];

    private static SiteAnalysisResultMetadata Metadata() =>
        new(
            SiteAnalysisType.EnvironmentalContext,
            "Al Barsha South",
            "no data source configured",
            SiteAnalysisConfidenceLevel.Low,
            DateTime.UtcNow,
            []);

    private static IReadOnlyList<JsonElement> Blocks(JsonDocument document) =>
        [.. document.RootElement.GetProperty("blocks").EnumerateArray()];

    [Fact]
    public void ComposeSections_ShouldEmitOnlyAllowedBlockKinds()
    {
        using var document = new SiteAnalysisContentComposer().ComposeSections(
            "Environmental Context",
            "Body text.",
            [
                new SiteAnalysisContentSection("Flood risk", [new SiteAnalysisContentItem("Flood zone", "No data source available")]),
                new SiteAnalysisContentSection("Sun and shadow", [], "A reference, not a figure."),
                new SiteAnalysisContentSection("Not assessed", [], "Outside scope."),
            ],
            Metadata());

        Blocks(document)
            .Select(block => block.GetProperty("kind").GetString())
            .Should().OnlyContain(kind => AllowedKinds.Contains(kind));
    }

    [Fact]
    public void ComposeSections_ShouldPutTheProvenanceBlockLast()
    {
        using var document = new SiteAnalysisContentComposer().ComposeSections(
            "Environmental Context",
            "Body text.",
            [new SiteAnalysisContentSection("Flood risk", [new SiteAnalysisContentItem("Flood zone", "No data source available")])],
            Metadata());

        var last = Blocks(document)[^1];
        last.GetProperty("kind").GetString().Should().Be("keyValue");
        last.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("label").GetString())
            .Should().ContainInOrder("Analysis", "Data source", "Confidence", "Generated");
    }

    [Fact]
    public void ComposeSections_ShouldEmitATextBlockForASectionBody_AndNoKeyValueWhenThereAreNoItems()
    {
        using var document = new SiteAnalysisContentComposer().ComposeSections(
            "Environmental Context",
            "Body text.",
            [new SiteAnalysisContentSection("Sun and shadow", [], "A reference, not a figure.")],
            Metadata());

        var blocks = Blocks(document);
        blocks.Should().Contain(block =>
            block.GetProperty("kind").GetString() == "text" &&
            block.GetProperty("text").GetString() == "A reference, not a figure.");
        blocks.Count(block => block.GetProperty("kind").GetString() == "keyValue").Should().Be(1, "only the provenance block carries values");
    }

    [Fact]
    public void ComposeSections_ShouldKeepTheEnvelopeVersion()
    {
        using var document = new SiteAnalysisContentComposer().ComposeSections(
            "Environmental Context", "Body text.", [], Metadata());

        document.RootElement.GetProperty("version").GetInt32().Should().Be(1);
    }
}
