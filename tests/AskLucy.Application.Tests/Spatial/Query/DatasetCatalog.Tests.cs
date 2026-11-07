using AskLucy.Application.Spatial.Query;

namespace AskLucy.Application.Tests.Spatial.Query;

/// <summary>
/// specs/095 — unit tests for <see cref="DatasetCatalog"/>: known and unknown lookups and the
/// four initial datasets.
/// </summary>
public class DatasetCatalogTests
{
    private static DatasetCatalog CreateCatalog() =>
        new([
            DatasetCatalog.Buildings,
            DatasetCatalog.Roads,
            DatasetCatalog.LandParcels,
            DatasetCatalog.DevelopmentAreas,
        ]);

    [Fact]
    public void TryGet_KnownDataset_ReturnsTrue()
    {
        var found = CreateCatalog().TryGet("buildings", out var dataset);

        Assert.True(found);
        Assert.NotNull(dataset);
        Assert.Equal("buildings", dataset.Id);
    }

    [Fact]
    public void TryGet_UnknownDataset_ReturnsFalse()
    {
        var found = CreateCatalog().TryGet("nonexistent", out var dataset);

        Assert.False(found);
        Assert.Null(dataset);
    }

    [Fact]
    public void ListAvailable_ReturnsAllFourDatasets()
    {
        var datasets = CreateCatalog().ListAvailable();

        Assert.Equal(4, datasets.Count);
        Assert.Contains(datasets, d => d.Id == "buildings");
        Assert.Contains(datasets, d => d.Id == "land-parcels");
        Assert.Contains(datasets, d => d.Id == "roads");
        Assert.Contains(datasets, d => d.Id == "development-areas");
    }

    [Fact]
    public void DescribeAvailable_NamesEveryDataset()
    {
        var description = CreateCatalog().DescribeAvailable();

        Assert.Contains("Buildings", description);
        Assert.Contains("Land Parcels", description);
        Assert.Contains("Roads", description);
        Assert.Contains("Development Areas", description);
    }
}
