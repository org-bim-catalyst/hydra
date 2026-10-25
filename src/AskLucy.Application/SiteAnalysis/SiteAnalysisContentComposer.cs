using System.Text.Json;
using System.Text.Json.Nodes;

namespace AskLucy.Application.SiteAnalysis;

/// <summary>
/// Builds the panel content-block document every site-analysis specialist reports through
/// <see cref="ISiteAnalysisResultRelay"/> (contracts/result-relay.md, research.md D9). Emits only
/// block kinds the existing client vocabulary already renders — <c>heading</c>, <c>text</c>,
/// <c>keyValue</c>, <c>image</c>, <c>divider</c> — matching the envelope
/// <c>PresentPanelContentCapability</c> already produces: <c>{"version":1,"blocks":[...]}</c>. No
/// new block kind is introduced (plan.md &#167;1.5/research.md D9).
/// </summary>
public sealed class SiteAnalysisContentComposer
{
    // CA1822: both members are kept instance rather than static — this is a DI-registered service
    // consumed as a constructor dependency (SiteSchematicImageGenerationTool, and every specialist
    // that follows), matching AgentDuplicateToolCallDetector's own precedent. A static composer
    // would be a static service class, which the constitution rules out.
#pragma warning disable CA1822
    private const int VocabularyVersion = 1;

    /// <summary>Composes a heading + body text + (optional) image + a trailing provenance block. The provenance block is always last (FR-011) so a reader sees the finding before its sourcing.</summary>
    public JsonDocument ComposeFinding(string heading, string bodyText, (Guid FileId, string Alt)? image, SiteAnalysisResultMetadata metadata)
    {
        var blocks = new JsonArray
        {
            HeadingBlock(heading),
            TextBlock(bodyText),
        };

        if (image is { } img)
        {
            blocks.Add(ImageBlock(img.FileId, img.Alt));
        }

        blocks.Add(BuildProvenanceBlock(metadata));

        var envelope = new JsonObject
        {
            ["version"] = VocabularyVersion,
            ["blocks"] = blocks,
        };

        return JsonSerializer.SerializeToDocument(envelope);
    }

    /// <summary>
    /// Composes a heading + body text + one divider-separated section per labelled group of values,
    /// with the provenance block last (FR-011). specs/088 — the environmental finding needs several
    /// labelled groups (flood risk, climate and wind, sun and shadow, not assessed) that
    /// <see cref="ComposeFinding"/>'s fixed heading+body+image shape cannot express. It emits the
    /// same block kinds and the same envelope, so the client needs no change.
    /// </summary>
    public JsonDocument ComposeSections(
        string heading,
        string bodyText,
        IReadOnlyList<SiteAnalysisContentSection> sections,
        SiteAnalysisResultMetadata metadata)
    {
        var blocks = new JsonArray
        {
            HeadingBlock(heading),
            TextBlock(bodyText),
        };

        foreach (var section in sections)
        {
            blocks.Add(DividerBlock());
            blocks.Add(HeadingBlock(section.Title));

            if (!string.IsNullOrWhiteSpace(section.BodyText))
            {
                blocks.Add(TextBlock(section.BodyText));
            }

            if (section.Items.Count > 0)
            {
                blocks.Add(KeyValueBlock(section.Items));
            }
        }

        blocks.Add(BuildProvenanceBlock(metadata));

        var envelope = new JsonObject
        {
            ["version"] = VocabularyVersion,
            ["blocks"] = blocks,
        };

        return JsonSerializer.SerializeToDocument(envelope);
    }

    /// <summary>
    /// data-model.md § "Confidence assignment rule" rendered as a <c>keyValue</c> block (FR-011) —
    /// analysis type, data source, confidence level, and generation time. Public so a future
    /// specialist composing its own multi-block layout (rather than <see cref="ComposeFinding"/>'s
    /// fixed heading+body+image shape) can still append the exact same provenance block. Never fed
    /// from <c>GeocodingCandidate.Importance</c> (research.md D10 — that field is on incompatible
    /// scales across providers; this composer never even sees it).
    /// </summary>
    public JsonObject BuildProvenanceBlock(SiteAnalysisResultMetadata metadata) =>
        new()
        {
            ["kind"] = "keyValue",
            ["items"] = new JsonArray
            {
                KeyValueItem("Analysis", metadata.AnalysisType.ToString()),
                KeyValueItem("Data source", metadata.DataSource),
                KeyValueItem("Confidence", metadata.ConfidenceLevel.ToString()),
                KeyValueItem("Generated", metadata.GeneratedAtUtc.ToString("u")),
            },
        };

    private static JsonObject KeyValueItem(string label, string value) =>
        new() { ["label"] = label, ["value"] = value };

    private static JsonObject HeadingBlock(string text) =>
        new() { ["kind"] = "heading", ["text"] = text, ["level"] = 2 };

    private static JsonObject TextBlock(string text) =>
        new() { ["kind"] = "text", ["text"] = text };

    private static JsonObject ImageBlock(Guid fileId, string alt) =>
        new() { ["kind"] = "image", ["fileId"] = fileId.ToString(), ["alt"] = alt };

    private static JsonObject DividerBlock() =>
        new() { ["kind"] = "divider" };

    private static JsonObject KeyValueBlock(IReadOnlyList<SiteAnalysisContentItem> items)
    {
        var itemArray = new JsonArray();
        foreach (var item in items)
        {
            itemArray.Add(KeyValueItem(item.Label, item.Value));
        }

        return new JsonObject { ["kind"] = "keyValue", ["items"] = itemArray };
    }
#pragma warning restore CA1822
}

/// <summary>
/// One labelled group inside a multi-section finding — e.g. "Flood risk". Carries a title, an
/// optional body paragraph, and optional labelled values; the composer emits whichever are present.
/// </summary>
public sealed record SiteAnalysisContentSection(
    string Title,
    IReadOnlyList<SiteAnalysisContentItem> Items,
    string? BodyText = null);

/// <summary>One label/value row inside a <see cref="SiteAnalysisContentSection"/>.</summary>
public sealed record SiteAnalysisContentItem(string Label, string Value);
