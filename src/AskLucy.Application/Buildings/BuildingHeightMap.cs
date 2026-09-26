namespace AskLucy.Application.Buildings;

/// <summary>
/// specs/076 — measured roof heights on a <see cref="GeoGrid"/>: each cell holds the height above
/// ground of the tallest roof over it, or <see cref="float.NaN"/> where nothing was measured.
/// A raster rather than one height per building because one source "building" can be a whole
/// complex: Esri models BurJuman's mall and its tower as a single feature whose only height
/// attribute is the tower top, so any per-building height paints the whole mall 105 m tall.
/// </summary>
public sealed class BuildingHeightMap
{
    private readonly float[] _heights;

    public BuildingHeightMap(GeoGrid grid, float[] heights)
    {
        if (heights.Length != grid.Width * grid.Height)
            throw new ArgumentException("There must be exactly one height per grid cell.", nameof(heights));
        Grid = grid;
        _heights = heights;
    }

    public static BuildingHeightMap Empty { get; } = new(new GeoGrid(0, 0, 1e-9, 1e-9, 1, 1), [float.NaN]);

    public GeoGrid Grid { get; }

    /// <summary>The height at a cell, or <see cref="float.NaN"/> where nothing was measured.</summary>
    public float this[int x, int y] => _heights[(y * Grid.Width) + x];

    /// <summary>Whether any cell carries a measurement.</summary>
    public bool HasMeasurements => _heights.Any(h => !float.IsNaN(h));
}
