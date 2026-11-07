namespace AskLucy.Application.Spatial.Query;

/// <summary>
/// specs/095 — registry of queryable geographic datasets. Implemented in-memory for v1;
/// extensible by registration without code changes to the query engine (SC-008).
/// </summary>
public interface IDatasetCatalog
{
    /// <summary>Resolves a dataset by id. Returns false when the id is not registered.</summary>
    bool TryGet(string datasetId, out DatasetEntry entry);

    /// <summary>Returns all registered entries, ordered by display name.</summary>
    IReadOnlyList<DatasetEntry> ListAvailable();

    /// <summary>Describes available datasets in a user-facing list for clarification messages.</summary>
    string DescribeAvailable();
}
