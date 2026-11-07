namespace AskLucy.Application.Spatial.Query;

/// <summary>
/// specs/095 — describes one queryable geographic dataset registered in <see cref="IDatasetCatalog"/>.
/// </summary>
/// <param name="Id">Unique lower-kebab-case identifier (e.g. <c>land-parcels</c>).</param>
/// <param name="DisplayName">User-facing name (e.g. "Land Parcels").</param>
/// <param name="AttributeFields">The fields users may filter on.</param>
/// <param name="SupportedRelationships">Spatial relationships this dataset's provider supports.</param>
/// <param name="ProviderId">Identifies the <see cref="ISpatialQueryProvider"/> that handles queries.</param>
public sealed record DatasetEntry(
    string Id,
    string DisplayName,
    IReadOnlyList<AttributeField> AttributeFields,
    IReadOnlyList<SpatialRelationship> SupportedRelationships,
    string ProviderId);
