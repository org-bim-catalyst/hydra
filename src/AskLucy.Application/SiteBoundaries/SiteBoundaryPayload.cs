using System.Text.Json;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.SiteBoundaries;

/// <summary>
/// specs/077 — the one JSON shape a boundary travels in between a capability's result and the
/// <c>__SITE_BOUNDARY__</c> event. Both <c>resolve_site_boundary</c> and
/// <c>set_site_boundary_members</c> write it and <c>StructuredPayloadExtractor</c> reads it, so
/// the two capabilities cannot drift apart on a field the viewer needs.
/// </summary>
public static class SiteBoundaryPayload
{
    public static JsonDocument Write(ConfirmedSiteBoundaryData boundary) =>
        JsonSerializer.SerializeToDocument(new
        {
            siteName = boundary.SiteName,
            areaSquareMeters = boundary.AreaSquareMeters,
            confidenceLevel = boundary.ConfidenceLevel.ToString(),
            source = boundary.SourceDetail,
            centroidLatitude = boundary.CentroidLatitude,
            centroidLongitude = boundary.CentroidLongitude,
            confidence = boundary.Confidence,
            sourceType = boundary.Source.ToString(),
            polygon = Points(boundary.Polygon),
            alternativeCandidateNames = boundary.AlternativeCandidateNames,
            corePolygon = boundary.CorePolygon is { } core ? Points(core) : null,
            additionalPolygons = boundary.AdditionalPolygons.Select(Points),

            // What the narrating model reads to tell the user which buildings the outline covers.
            includedBuildings = boundary.Members.Where(m => m.Included).Select(m => m.Name),
            excludedBuildings = boundary.Members.Where(m => !m.Included).Select(m => m.Name),
            members = boundary.Members.Select(m => new
            {
                id = m.Id,
                name = m.Name,
                kind = m.Kind.ToString(),
                relation = m.Relation.ToString(),
                gapMeters = Math.Round(m.GapMeters, 1),
                included = m.Included,
                ring = Points(m.Ring),
            }),
        });

    /// <summary>Rebuilds the boundary; throws <see cref="KeyNotFoundException"/>/<see cref="ArgumentException"/>/<see cref="InvalidOperationException"/> on a malformed payload, for the caller to treat as "no payload".</summary>
    public static ConfirmedSiteBoundaryData Read(JsonElement root) =>
        new(
            root.GetProperty("siteName").GetString() ?? string.Empty,
            root.GetProperty("centroidLatitude").GetDouble(),
            root.GetProperty("centroidLongitude").GetDouble(),
            ReadPoints(root.GetProperty("polygon")),
            root.GetProperty("areaSquareMeters").GetDouble(),
            root.TryGetProperty("confidence", out var confidence) ? confidence.GetDouble() : 1d,
            Enum.Parse<BoundaryConfidenceLevel>(root.GetProperty("confidenceLevel").GetString()!),
            Enum.Parse<SiteBoundarySource>(root.GetProperty("sourceType").GetString()!),
            root.TryGetProperty("source", out var source) ? source.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("alternativeCandidateNames", out var alternatives)
                ? [.. alternatives.EnumerateArray().Select(a => a.GetString() ?? string.Empty)]
                : [])
        {
            CorePolygon = root.TryGetProperty("corePolygon", out var core) && core.ValueKind == JsonValueKind.Array
                ? ReadPoints(core)
                : null,
            AdditionalPolygons = root.TryGetProperty("additionalPolygons", out var additional)
                ? [.. additional.EnumerateArray().Select(ReadPoints)]
                : [],
            Members = root.TryGetProperty("members", out var members)
                ? [.. members.EnumerateArray().Select(m => new SiteBoundaryMember(
                    m.GetProperty("id").GetString() ?? string.Empty,
                    m.GetProperty("name").GetString() ?? string.Empty,
                    Enum.Parse<SiteBoundaryMemberKind>(m.GetProperty("kind").GetString()!),
                    Enum.Parse<SiteBoundaryMemberRelation>(m.GetProperty("relation").GetString()!),
                    m.GetProperty("gapMeters").GetDouble(),
                    ReadPoints(m.GetProperty("ring")),
                    m.GetProperty("included").GetBoolean()))]
                : [],
        };

    private static IEnumerable<object> Points(IReadOnlyList<GeoPoint> ring) =>
        ring.Select(p => new { latitude = p.Latitude, longitude = p.Longitude });

    private static IReadOnlyList<GeoPoint> ReadPoints(JsonElement array) =>
        [.. array.EnumerateArray().Select(p => new GeoPoint(p.GetProperty("latitude").GetDouble(), p.GetProperty("longitude").GetDouble()))];
}
