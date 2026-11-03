using AskLucy.Domain.Abstractions;

namespace AskLucy.Domain.DatasetSources;

/// <summary>
/// Represents a data source from an open data provider, with metadata for relevance classification.
/// </summary>
public class DataSource : AggregateRoot<Guid>
{
    /// <summary>
    /// Unique identifier of the data source.
    /// </summary>
    public override Guid Id { get; protected set; }

    /// <summary>
    /// Name of the data source (e.g., "Oman Open Data Portal").
    /// </summary>
    public string Name { get; protected set; } = string.Empty;

    /// <summary>
    /// URL of the data source endpoint.
    /// </summary>
    public string Url { get; protected set; } = string.Empty;

    /// <summary>
    /// Categorization of the source: Oman-specific, General, or Mixed.
    /// </summary>
    public DataSourceCategory Category { get; protected set; } = DataSourceCategory.Unknown;

    /// <summary>
    /// Metadata about the source (last updated, description, etc.) stored as JSON.
    /// </summary>
    public string MetadataJson { get; protected set; } = "{}";

    /// <summary>
    /// Timestamp when the source data was last updated (UTC).
    /// </null>
    public DateTime LastUpdatedUtc { get; protected set; }

    /// <summary>
    /// Date when the source entry was created (UTC).
    /// </summary>
    public DateTime CreatedAtUtc { get; protected set; }

    /// <summary>
    /// Date when the source entry was last modified (UTC).
    /// </summary>
    public DateTime LastModifiedUtc { get; protected set; }

    /// <summary>
    /// Protected constructor for EF Core and pattern compliance.
    /// </summary>
    protected DataSource()
    {
        Id = Guid.NewGuid();
        CreatedAtUtc = DateTime.UtcNow;
        LastModifiedUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Creates a new data source entity.
    /// </summary>
    /// <param name="name">Name of the data source.</param>
    /// <param name="url">URL of the data source endpoint.</param>
    /// <param name="category">Initial categorization.</param>
    /// <param name="metadataJson">Optional metadata as JSON string.</param>
    public DataSource(string name, string url, DataSourceCategory category, string metadataJson = "{}")
        : this()
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
        LastModifiedUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Updates the source's category classification.
    /// </summary>
    /// <param name="category">New category.</param>
    public void UpdateCategory(DataSourceCategory category)
    {
        Category = category;
        LastModifiedUtc = DateTime.UtcNow;
    }
}