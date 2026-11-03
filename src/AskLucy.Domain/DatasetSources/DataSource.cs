using AskLucy.Domain.Common;

namespace AskLucy.Domain.DatasetSources;

/// <summary>
/// An open data source registered for dataset/source evaluation, with the metadata needed to
/// classify its relevance. Aggregate root of the <c>DatasetSources</c> context.
/// </summary>
public sealed class DataSource : BaseEntity
{
    /// <summary>
    /// Name of the data source (e.g., "Oman Open Data Portal").
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// URL of the data source endpoint.
    /// </summary>
    public string Url { get; private set; } = string.Empty;

    /// <summary>
    /// Categorization of the source: Oman-specific, General, Mixed, or Unknown.
    /// </summary>
    public DataSourceCategory Category { get; private set; } = DataSourceCategory.Unknown;

    /// <summary>
    /// Metadata about the source (description, last updated, etc.) stored as JSON.
    /// </summary>
    public string MetadataJson { get; private set; } = "{}";

    /// <summary>
    /// Timestamp when the source data was last updated (UTC).
    /// </summary>
    public DateTime LastUpdatedUtc { get; private set; }

    /// <summary>
    /// Required by EF Core materialization.
    /// </summary>
    private DataSource()
    {
    }

    /// <summary>
    /// Creates a new data source entity.
    /// </summary>
    /// <param name="name">Name of the data source.</param>
    /// <param name="url">URL of the data source endpoint.</param>
    /// <param name="category">Initial categorization.</param>
    /// <param name="metadataJson">Optional metadata as JSON string.</param>
    public DataSource(string name, string url, DataSourceCategory category, string metadataJson = "{}")
    {
        Name = name;
        Url = url;
        Category = category;
        MetadataJson = metadataJson;
        LastUpdatedUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Updates the source's metadata and recency timestamp.
    /// </summary>
    /// <param name="metadataJson">New metadata as JSON string.</param>
    /// <param name="lastUpdatedUtc">New last-updated timestamp.</param>
    public void UpdateMetadata(string metadataJson, DateTime lastUpdatedUtc)
    {
        MetadataJson = metadataJson;
        LastUpdatedUtc = lastUpdatedUtc;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Updates the source's category classification.
    /// </summary>
    /// <param name="category">New category.</param>
    public void UpdateCategory(DataSourceCategory category)
    {
        Category = category;
        ModifiedAtUtc = DateTime.UtcNow;
    }
}
