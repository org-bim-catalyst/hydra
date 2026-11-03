using System.Text.Json;
using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.SiteAnalysis;

/// <summary>
/// The parsed form of <c>SiteAnalysis.BoundaryGeoJson</c>. <see cref="Ring"/> is null when there is
/// nothing measurable; <see cref="UnavailableReason"/> then says whether the analysis simply had no
/// boundary or whether the stored snapshot could not be read — two different facts the finding must
/// not collapse into one.
/// </summary>
public sealed record SiteBoundarySnapshotResult(IReadOnlyList<GeoPoint>? Ring, string? UnavailableReason)
{
    public bool HasRing => Ring is not null;
}

/// <summary>
/// specs/089-site-geometry-specialist data-model.md § 1 — reads the frozen boundary snapshot written
/// at dispatch. The snapshot is the analysis's own record of the site, so it is the measurement
/// basis; re-resolving the site could describe a different place (SPEC-090's rule, applied here).
/// Never throws: a missing or unreadable snapshot is a stated reason, not an exception.
/// </summary>
public static class SiteBoundarySnapshot
{
    public const string MissingReason = "No boundary was resolved for this analysis.";
    public const string UnreadableReason = "The analysis's boundary snapshot could not be read.";

    public static SiteBoundarySnapshotResult TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new SiteBoundarySnapshotResult(null, MissingReason);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new SiteBoundarySnapshotResult(null, UnreadableReason);
            }

            var ring = new List<GeoPoint>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object ||
                    !TryReadCoordinate(element, "latitude", out var latitude) ||
                    !TryReadCoordinate(element, "longitude", out var longitude))
                {
                    return new SiteBoundarySnapshotResult(null, UnreadableReason);
                }

                ring.Add(new GeoPoint(latitude, longitude));
            }

            return ring.Count >= 3
                ? new SiteBoundarySnapshotResult(ring, null)
                : new SiteBoundarySnapshotResult(null, UnreadableReason);
        }
        catch (JsonException)
        {
            return new SiteBoundarySnapshotResult(null, UnreadableReason);
        }
    }

    /// <summary>
    /// The snapshot is written by the default serializer (PascalCase) today, but accepting the
    /// camelCase form costs nothing and keeps a future serializer change from silently turning every
    /// boundary into "unreadable".
    /// </summary>
    private static bool TryReadCoordinate(JsonElement element, string name, out double value)
    {
        value = 0.0;
        var property = char.ToUpperInvariant(name[0]) + name[1..];
        if (!element.TryGetProperty(name, out var candidate) && !element.TryGetProperty(property, out candidate))
        {
            return false;
        }

        return candidate.ValueKind == JsonValueKind.Number && candidate.TryGetDouble(out value);
    }
}
