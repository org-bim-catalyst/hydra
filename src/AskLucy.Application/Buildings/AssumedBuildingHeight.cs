namespace AskLucy.Application.Buildings;

/// <summary>
/// specs/052 research D5 — the height every footprint source gives a building it knows nothing
/// about: three storeys at 3 m. One constant so the sources cannot drift apart, and so a merge can
/// tell this bare default from an assumption that was derived from something (a storey count).
/// </summary>
public static class AssumedBuildingHeight
{
    public const double DefaultMetres = 9.0;

    /// <summary>The height a building gets per storey when only its storey count is known.</summary>
    public const double MetresPerStorey = 3.0;

    public static bool IsBareDefault(BuildingFootprint building) =>
        building.HeightProvenance == BuildingHeightProvenance.Assumed && building.HeightMetres == DefaultMetres;
}
