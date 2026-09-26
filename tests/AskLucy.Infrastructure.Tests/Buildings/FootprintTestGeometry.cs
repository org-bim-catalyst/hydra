using AskLucy.Application.Buildings;
using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Infrastructure.Tests.Buildings;

/// <summary>Footprints and height maps laid out in metres east/north of a centre point, so a test reads as a site plan.</summary>
internal static class FootprintTestGeometry
{
    public static readonly GeoPoint Center = new(25.1560, 55.2218);

    private const double MetresPerDegree = 111_320.0;

    public static GeoPoint At(double east, double north) => new(
        Center.Latitude + (north / MetresPerDegree),
        Center.Longitude + (east / (MetresPerDegree * Math.Cos(Center.Latitude * Math.PI / 180))));

    public static BuildingFootprint Rect(
        string id, double west, double south, double east, double north,
        double height = AssumedBuildingHeight.DefaultMetres,
        BuildingHeightProvenance provenance = BuildingHeightProvenance.Assumed,
        bool site = false) =>
        new(id,
            [At(west, south), At(east, south), At(east, north), At(west, north), At(west, south)],
            height, provenance, string.Empty, site);

    /// <summary>A 2 m height map over ±<paramref name="halfExtentMetres"/>, each cell's height read at its centre.</summary>
    public static BuildingHeightMap Map(Func<double, double, float> heightAtMetres, double halfExtentMetres = 300)
    {
        var grid = GeoGrid.Around(Center, halfExtentMetres, 2.0);
        var heights = new float[grid.Width * grid.Height];
        for (var y = 0; y < grid.Height; y++)
        {
            for (var x = 0; x < grid.Width; x++)
            {
                var point = grid.CellCentre(x, y);
                var east = (point.Longitude - Center.Longitude) * MetresPerDegree * Math.Cos(Center.Latitude * Math.PI / 180);
                var north = (point.Latitude - Center.Latitude) * MetresPerDegree;
                heights[(y * grid.Width) + x] = heightAtMetres(east, north);
            }
        }

        return new BuildingHeightMap(grid, heights);
    }

    public static bool Inside(double east, double north, double west, double south, double eastEdge, double northEdge) =>
        east >= west && east < eastEdge && north >= south && north < northEdge;
}
