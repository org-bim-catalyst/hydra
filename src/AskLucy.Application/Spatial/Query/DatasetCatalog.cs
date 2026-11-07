namespace AskLucy.Application.Spatial.Query;

/// <summary>
/// specs/095 — in-memory <see cref="IDatasetCatalog"/> pre-populated with the four initial
/// datasets. New datasets are added by registering additional <see cref="DatasetEntry"/> values
/// in DI without changing this class (SC-008 / research D6).
/// </summary>
public sealed class DatasetCatalog : IDatasetCatalog
{
    private readonly IReadOnlyDictionary<string, DatasetEntry> _entries;

    public DatasetCatalog(IEnumerable<DatasetEntry> entries)
    {
        _entries = entries.ToDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase);
    }

    public bool TryGet(string datasetId, out DatasetEntry entry) =>
        _entries.TryGetValue(datasetId, out entry!);

    public IReadOnlyList<DatasetEntry> ListAvailable() =>
        [.. _entries.Values.OrderBy(e => e.DisplayName)];

    public string DescribeAvailable() =>
        string.Join(", ", ListAvailable().Select(e => $"\"{e.DisplayName}\""));

    // ── Initial Dataset Catalog (specs/095 Assumptions) ──────────────────────────────────────

    public static readonly DatasetEntry LandParcels = new(
        Id: "land-parcels",
        DisplayName: "Land Parcels",
        AttributeFields:
        [
            new("area",     "Area",           AttributeFieldType.Numeric, "m2",   "5000"),
            new("land-use", "Land Use",       AttributeFieldType.String,  null,   "Commercial"),
            new("zone",     "Zone",           AttributeFieldType.String,  null,   "Development Zone"),
        ],
        SupportedRelationships: [SpatialRelationship.Within, SpatialRelationship.Outside,
            SpatialRelationship.Intersecting, SpatialRelationship.Containing],
        ProviderId: "overpass");

    public static readonly DatasetEntry Buildings = new(
        Id: "buildings",
        DisplayName: "Buildings",
        AttributeFields:
        [
            new("height",     "Height",         AttributeFieldType.Numeric, "m",    "30"),
            new("floors",     "Floors",         AttributeFieldType.Numeric, null,   "5"),
            new("year-built", "Year Built",     AttributeFieldType.Year,    null,   "1990"),
            new("use",        "Building Use",   AttributeFieldType.String,  null,   "Residential"),
        ],
        SupportedRelationships: [SpatialRelationship.Within, SpatialRelationship.Outside,
            SpatialRelationship.Intersecting, SpatialRelationship.Containing, SpatialRelationship.Near],
        ProviderId: "overpass");

    public static readonly DatasetEntry Roads = new(
        Id: "roads",
        DisplayName: "Roads",
        AttributeFields:
        [
            new("name",           "Road Name",      AttributeFieldType.String, null, "King Fahd Road"),
            new("classification", "Classification", AttributeFieldType.String, null, "primary"),
            new("surface",        "Surface",        AttributeFieldType.String, null, "asphalt"),
        ],
        SupportedRelationships: [SpatialRelationship.Within, SpatialRelationship.Intersecting],
        ProviderId: "overpass");

    public static readonly DatasetEntry DevelopmentAreas = new(
        Id: "development-areas",
        DisplayName: "Development Areas",
        AttributeFields:
        [
            new("name",   "Name",   AttributeFieldType.String,  null, "Al Khuwair Zone"),
            new("status", "Status", AttributeFieldType.String,  null, "Active"),
            new("area",   "Area",   AttributeFieldType.Numeric, "m2", "50000"),
        ],
        SupportedRelationships: [SpatialRelationship.Within, SpatialRelationship.Outside,
            SpatialRelationship.Intersecting, SpatialRelationship.Containing],
        ProviderId: "overpass");
}
