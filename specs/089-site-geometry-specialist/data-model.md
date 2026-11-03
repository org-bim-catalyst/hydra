# Phase 1 Data Model: Site Geometry Specialist

**No persistence change.** This feature adds no table, column, index or migration. The finding is a
`SiteAnalysisResult` (SPEC-057), written through the existing relay. Everything below is either an
in-memory record or a contract on data that already exists.

## 1. Persisted data read by this feature

### `SiteAnalysis` (existing, unchanged)

| Field | Use |
|---|---|
| `Id` | The specialist's input and the relay's key |
| `SiteName` | Heading, body, provenance |
| `Latitude`, `Longitude` | Centre of the source query; `Contains` test for the parcel |
| `BoundaryGeoJson` | The frozen site polygon — the measurement basis |
| `UserId`, `UserChatId` | Resolved by the relay for delivery; never read by the tool |

### `BoundaryGeoJson` snapshot format

Written by `RequestSiteAnalysisCapability` as `JsonSerializer.Serialize(boundary.Polygon)` over
`IReadOnlyList<GeoPoint>`:

```json
[ { "latitude": 25.156, "longitude": 55.221 }, { "latitude": 25.156, "longitude": 55.222 }, … ]
```

**Parse rules** (`SiteBoundarySnapshot.TryParse`):

| Input | Result |
|---|---|
| `null` / empty | no boundary, reason "no boundary was resolved for this analysis" |
| JSON array of ≥ 3 objects with numeric `latitude`/`longitude` (any casing) | ring, open or closed |
| JSON array with < 3 points, a non-object element, non-numeric coordinates, or invalid JSON | no boundary, reason "the analysis's boundary snapshot could not be read"; logged at Warning by the tool |

The parser never throws. It also accepts a closed ring (repeated first point) and normalises it to a
ring whose last point may repeat — `GeometryMath` treats both forms correctly.

## 2. Domain change

`SiteAnalysisType` gains one member, appended after the existing three (enums serialise as strings
API-wide; order is not part of the contract):

```
SchematicImage | UrbanContext | EnvironmentalContext | SiteGeometry
```

## 3. Application records (new)

### `OverpassTagFilter` — `Application/Overpass/OverpassFeatureQuery.cs`

```csharp
public sealed record OverpassTagFilter(string Key, IReadOnlyList<string>? Values, bool IncludeRelations = false);
```
`Values == null` means "any value" and matches ways only (relations are never queried for an
any-value filter). Explicit values also match `relation["type"="multipolygon"]` when
`IncludeRelations` is true.

### `OverpassFeatureQuery`

```csharp
public sealed record OverpassFeatureQuery(
    GeoPoint Center,
    int RadiusMeters,
    IReadOnlyList<OverpassTagFilter> Filters);
```

### `OverpassFeature`

```csharp
public sealed record OverpassFeature(
    string Id,                 // "osm_way_123" / "osm_relation_456"
    string ElementType,        // "way" | "relation"
    IReadOnlyDictionary<string, string> Tags,
    IReadOnlyList<GeoPoint> Geometry);
```
A way's geometry is returned as mapped (open or closed). A relation's geometry is its largest
assembled outer ring; a relation that cannot close is omitted.

### `SiteGeometryFeatureData` — `Application/SiteAnalysis/Providers/SiteGeometryFeatureData.cs`

```csharp
public sealed record SiteGeometryParcel(string Id, IReadOnlyList<GeoPoint> Ring, IReadOnlyDictionary<string, string> Tags);
public sealed record SiteGeometryStreet(string Id, string? Name, IReadOnlyList<GeoPoint> Geometry);
public sealed record SiteGeometryFeatureData(
    IReadOnlyList<SiteGeometryParcel> Parcels,
    IReadOnlyList<SiteGeometryStreet> Streets);
```

Returned through the existing `DataResolutionOutcome<T>` vocabulary
(`Application/SiteAnalysis/Providers/DataResolutionOutcome.cs`):
`Confirmed(data)` when the query succeeded (even with empty lists), `Unavailable(reason)` when the
source could not be reached.

### `SiteGeometryMeasurements` — `Application/SiteAnalysis/SiteGeometry/SiteGeometryMeasurements.cs`

```csharp
public sealed record StreetFrontage(string StreetName, double LengthMeters);
public sealed record SiteGeometryMeasurements(
    double AreaSquareMeters,
    double LengthMeters,
    double WidthMeters,
    double? OrientationBearingDegrees,      // null when near-square
    IReadOnlyList<StreetFrontage> Frontages,
    bool LooksLikeResolvedBuffer);          // 24-vertex, equidistant ring
```

## 4. Calculation rules

| Figure | Rule | Unavailable when |
|---|---|---|
| Area | `GeometryMath.AreaSquareMeters(ring)` | ring missing / < 3 points / area ≤ 0 |
| Dimensions | principal-axis projection of vertices in local metres; larger extent first | ring missing / < 3 points |
| Orientation | bearing of the principal axis, `atan2(east, north)` normalised to 0–360, rendered as a compass name + degrees | near-square (extents within 5%) → reported "not meaningful", not "unavailable" |
| Frontage | per boundary edge, samples attributed to the nearest **named** street within 25 m; sum per street; total = sum | no boundary, source unavailable, or no named street within tolerance |
| Parcel | containing `(Latitude, Longitude)`, smallest area; reported separately from the measurement ring | none contains the point, or source unavailable |

Constants (named, not literal): `FrontageToleranceMeters = 25`, `MinimumQueryRadiusMeters = 60`,
`MaximumQueryRadiusMeters = 1_500`, `PointOnlyQueryRadiusMeters = 120`, `NearSquareRatio = 0.05`,
`RadiusMarginMeters = 80`.

## 5. Confidence assignment

`SiteAnalysisConfidence.ResolveConfidence(dataDirectlyDescribesSite, dataIsSparseOrApproximate)`:

| Situation | Arguments | Level |
|---|---|---|
| Parcel outline recorded and measured | `true, false` | **High** |
| No parcel; figures measured from the resolved site boundary | `false, true` | **Medium** |
| No boundary at all | `false, false` | **Low** |

Never derived from `GeocodingCandidate.Importance` (SPEC-057 research D10).

## 6. Finding content document

```json
{
  "version": 1,
  "blocks": [
    { "kind": "heading", "text": "Site Geometry", "level": 2 },
    { "kind": "text",    "text": "Measured geometry for {siteName}. …" },
    { "kind": "divider" },
    { "kind": "heading", "text": "Measurements", "level": 2 },
    { "kind": "keyValue", "items": [
        { "label": "Area", "value": "4,320 m² (0.43 ha)" },
        { "label": "Dimensions", "value": "120 m × 85 m" },
        { "label": "Orientation", "value": "north-east (48°)" } ] },
    { "kind": "divider" },
    { "kind": "heading", "text": "Frontage", "level": 2 },
    { "kind": "keyValue", "items": [
        { "label": "Sheikh Zayed Road", "value": "84 m" },
        { "label": "Total frontage", "value": "84 m" } ] },
    { "kind": "divider" },
    { "kind": "heading", "text": "Boundary", "level": 2 },
    { "kind": "text",    "text": "No parcel or plot boundary is recorded in OpenStreetMap for this site. …" },
    { "kind": "keyValue", "items": [ /* provenance */ ] }
  ]
}
```

Only `heading`, `text`, `divider` and `keyValue` are emitted. No `image`, no `table`, no new kind.
The provenance block is always last.

## 7. Result lifecycle

Unchanged. `SiteAnalysisResult` is `Completed`, `Failed` or `Rejected` exactly as every other
specialist's; this feature adds no state, and the analysis's closing outcome already counts the branch
because `SiteAnalysisSpecialists.Count` derives from the single declaration.
