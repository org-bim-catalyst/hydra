using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Infrastructure.Buildings.Esri;

/// <summary>
/// An I3S node's oriented bounding box in a global (WGS84) scene layer. The centre is longitude,
/// latitude and ellipsoidal height; the half sizes are metres along the box's own axes; the
/// quaternion (x, y, z, w) turns those axes into Earth-centred, Earth-fixed ones. The root node
/// shows the convention: its box sits 6.36 million metres below the surface with an identity
/// rotation, i.e. an axis-aligned box around the whole Earth. A flat lat/lon distance test cannot
/// handle that box, which is why the test runs in Earth-centred coordinates.
/// </summary>
internal readonly record struct I3sOrientedBoundingBox(
    double CenterLongitude,
    double CenterLatitude,
    double CenterHeight,
    double HalfSizeX,
    double HalfSizeY,
    double HalfSizeZ,
    double QuaternionX,
    double QuaternionY,
    double QuaternionZ,
    double QuaternionW)
{
    private const double SemiMajorAxis = 6_378_137.0;
    private const double EccentricitySquared = 6.69437999014e-3;

    /// <summary>Metres from <paramref name="point"/> (on the ellipsoid) to the nearest part of the box; 0 inside it.</summary>
    public double DistanceMetres(GeoPoint point)
    {
        var (px, py, pz) = ToEarthCentred(point.Latitude, point.Longitude, 0);
        var (cx, cy, cz) = ToEarthCentred(CenterLatitude, CenterLongitude, CenterHeight);
        var (dx, dy, dz) = (px - cx, py - cy, pz - cz);

        // The box's axes in Earth-centred coordinates are the rotation matrix's columns; projecting
        // the offset onto each gives the offset in the box's own frame.
        double x = QuaternionX, y = QuaternionY, z = QuaternionZ, w = QuaternionW;
        var outsideX = Outside(
            ((1 - (2 * ((y * y) + (z * z)))) * dx) + (2 * ((x * y) + (z * w)) * dy) + (2 * ((x * z) - (y * w)) * dz),
            HalfSizeX);
        var outsideY = Outside(
            (2 * ((x * y) - (z * w)) * dx) + ((1 - (2 * ((x * x) + (z * z)))) * dy) + (2 * ((y * z) + (x * w)) * dz),
            HalfSizeY);
        var outsideZ = Outside(
            (2 * ((x * z) + (y * w)) * dx) + (2 * ((y * z) - (x * w)) * dy) + ((1 - (2 * ((x * x) + (y * y)))) * dz),
            HalfSizeZ);
        return Math.Sqrt((outsideX * outsideX) + (outsideY * outsideY) + (outsideZ * outsideZ));
    }

    private static double Outside(double offset, double halfSize) => Math.Max(0, Math.Abs(offset) - halfSize);

    internal static (double X, double Y, double Z) ToEarthCentred(double latitude, double longitude, double height)
    {
        var phi = latitude * Math.PI / 180.0;
        var lambda = longitude * Math.PI / 180.0;
        var sinPhi = Math.Sin(phi);
        var primeVerticalRadius = SemiMajorAxis / Math.Sqrt(1 - (EccentricitySquared * sinPhi * sinPhi));
        return (
            (primeVerticalRadius + height) * Math.Cos(phi) * Math.Cos(lambda),
            (primeVerticalRadius + height) * Math.Cos(phi) * Math.Sin(lambda),
            ((primeVerticalRadius * (1 - EccentricitySquared)) + height) * sinPhi);
    }
}
