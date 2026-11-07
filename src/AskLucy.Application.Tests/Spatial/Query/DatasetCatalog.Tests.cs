using AskLucy.Application.Spatial.Query;
using Xunit;

namespace AskLucy.Application.Tests.Spatial.Query;

/// <summary>
/// specs/095 — unit tests for <see cref="DatasetCatalog"/>.
/// Covers: known resolves, unknown returns false, ListAvailable returns all 4.
/// </summary>
public class DatasetCatalogTests
{
    private readonly IDatasetCatalog _catalog;

    public DatasetCatalogTests()
    {
        // Reset singleton
        DatasetCatalog.Instance = new DatasetCatalog();
        _catalog = DatasetCatalog.Instance;
    }

    [Fact]
    public void TryGet_KnownDataset_ReturnsTrue()
    {
        // Act
        var result = _catalog.TryGet("buildings", out var dataset);

        // Assert
        Assert.True(result);
        Assert.NotNull(dataset);
        Assert.Equal("buildings", dataset.Id);
    }

    [Fact]
    public void TryGet_UnknownDataset_ReturnsFalse()
    {
        // Act
        var result = _catalog.TryGet("nonexistent", out var dataset);

        // Assert
        Assert.False(result);
        Assert.Null(dataset);
    }

    [Fact]
    public void ListAvailable_ReturnsAllFourDatasets()
    {
        // Act
        var datasets = _catalog.ListAvailable();

        // Assert
        Assert.Equal(4, datasets.Count);
        Assert.Contains(datasets, d => d.Id == "buildings");
        Assert.Contains(datasets, d => d.Id == "land-parcels");
        Assert.Contains(datasets, d => d.Id == "roads");
        Assert.Contains(datasets, d => d.Id == "development-areas");
    }

    [Fact]
    public void DescribeAvailable_ReturnsDescriptions()
    {
        // Act
        var descriptions = _catalog.DescribeAvailable();

        // Assert
        Assert.Contains(descriptions, d => d.Contains("buildings"));
        Assert.Contains(descriptions, d => d.Contains("land-parcels"));
        Assert.Contains(descriptions, d => d.Contains("roads"));
        Assert.Contains(descriptions, d => d.Contains("development-areas"));
    }
}