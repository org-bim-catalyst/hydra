namespace AskLucy.Domain.DatasetSources;

/// <summary>
/// Categorization of data sources: Oman-specific, General, or Mixed relevance.
/// </summary>
public enum DataSourceCategory
{
    /// <summary>
    /// Oman-specific source (focused on Oman geography, government, or demographics).
    /// </summary>
    OmanSpecific,

    /// <summary>
    /// General/International source (covers multiple countries/regions globally).
    /// </summary>
    General,

    /// <summary>
    /// Mixed relevance (contains both Oman-specific and general content).
    /// </summary>
    Mixed,

    /// <summary>
    /// Unknown category (could not determine).
    /// </summary>
    Unknown
}
