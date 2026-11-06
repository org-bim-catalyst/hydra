using AskLucy.Application.Spatial;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Spatial;

/// <summary>specs/094 — specialist output is untrusted; the normalizer is the validation gate.</summary>
public sealed class SpatialResultNormalizerTests
{
    private readonly SpatialResultNormalizer _normalizer = new();

    private static SpatialOperation Operation(string id = "op-1") =>
        new(id, SpatialOperationKinds.PoiDiscovery, [new FeatureSelector(Category: "school")]);

    private static SpatialLayerDraft MarkerDraft(string layerId, params (string Id, double Lat)[] markers) =>
        new(
            layerId,
            "op-1",
            "Layer",
            "Layer",
            SpatialLayerKind.Markers,
            [.. markers.Select(marker => new SpatialMarker(marker.Id, "Feature", marker.Lat, 58.3))]);

    [Fact]
    public void Normalize_ShouldDeduplicateMarkersAcrossLayers()
    {
        var result = _normalizer.Normalize("analysis-1", "bundle-1",
        [
            new SpatialSpecialistExecution(Operation(), "test", new SpatialSpecialistOutcome(
                SpatialOperationStatus.Succeeded,
                [MarkerDraft("a", ("m-1", 23.5), ("m-2", 23.6)), MarkerDraft("b", ("m-1", 23.5))],
                [])),
        ]);

        result.Layers.SelectMany(layer => layer.Markers!).Should().HaveCount(2);
        result.ResultCount.Should().Be(2);
    }

    [Fact]
    public void Normalize_ShouldDropMarkersWithInvalidCoordinates()
    {
        var result = _normalizer.Normalize("analysis-1", "bundle-1",
        [
            new SpatialSpecialistExecution(Operation(), "test", new SpatialSpecialistOutcome(
                SpatialOperationStatus.Succeeded,
                [MarkerDraft("a", ("m-1", 23.5), ("m-2", 200))],
                [])),
        ]);

        result.Layers.SelectMany(layer => layer.Markers!).Should().ContainSingle();
    }

    [Fact]
    public void Normalize_ShouldIgnoreAnAreaLayerWithAnInvalidOutline()
    {
        var draft = new SpatialLayerDraft(
            "buffer-1", "op-1", "Buffer", "Buffer", SpatialLayerKind.Buffer,
            Ring: [new GeoPoint(23.5, 58.3), new GeoPoint(23.6, 58.3)]);

        var result = _normalizer.Normalize("analysis-1", "bundle-1",
        [
            new SpatialSpecialistExecution(Operation(), "test", new SpatialSpecialistOutcome(
                SpatialOperationStatus.Succeeded, [draft], [])),
        ]);

        result.Layers.Should().BeEmpty();
        result.Warnings.Should().Contain(warning => warning.Contains("outline"));
        result.Status.Should().Be(SpatialAnalysisStatus.Partial);
    }

    [Fact]
    public void Normalize_ShouldStripUnknownAndUnsafeAttributes()
    {
        var draft = new SpatialLayerDraft(
            "a", "op-1", "Layer", "Layer", SpatialLayerKind.Markers,
            [new SpatialMarker("m-1", "Feature", 23.5, 58.3)],
            Attributes: new Dictionary<string, string>
            {
                ["category"] = "school",
                ["website"] = "https://example.com",
                ["address"] = "<script>alert(1)</script>",
            });

        var result = _normalizer.Normalize("analysis-1", "bundle-1",
        [
            new SpatialSpecialistExecution(Operation(), "test", new SpatialSpecialistOutcome(
                SpatialOperationStatus.Succeeded, [draft], [])),
        ]);

        var attributes = result.Layers[0].Attributes!;
        attributes.Should().ContainKey("category");
        attributes.Should().NotContainKey("website");
        attributes.Should().NotContainKey("address");
    }

    [Fact]
    public void Normalize_ShouldReportTheWorstCoverageForASource()
    {
        var result = _normalizer.Normalize("analysis-1", "bundle-1",
        [
            new SpatialSpecialistExecution(Operation("op-1"), "test", new SpatialSpecialistOutcome(
                SpatialOperationStatus.Succeeded,
                [MarkerDraft("a", ("m-1", 23.5))],
                [DataSourceCoverage.Complete("overpass", "OpenStreetMap")])),
            new SpatialSpecialistExecution(Operation("op-2"), "test", new SpatialSpecialistOutcome(
                SpatialOperationStatus.Empty, [], [DataSourceCoverage.Unavailable("overpass", "OpenStreetMap")])),
        ]);

        result.Coverage.Should().ContainSingle();
        result.Coverage[0].CoverageStatus.Should().Be("Unavailable");
    }

    [Fact]
    public void Normalize_ShouldMarkAPartialResultWhenOneOperationFailed()
    {
        var result = _normalizer.Normalize("analysis-1", "bundle-1",
        [
            new SpatialSpecialistExecution(Operation("op-1"), "test", new SpatialSpecialistOutcome(
                SpatialOperationStatus.Succeeded, [MarkerDraft("a", ("m-1", 23.5))], [])),
            new SpatialSpecialistExecution(Operation("op-2"), "test", null, new InvalidOperationException("down")),
        ]);

        result.Status.Should().Be(SpatialAnalysisStatus.Partial);
        result.Layers.Should().HaveCount(1);
    }
}
