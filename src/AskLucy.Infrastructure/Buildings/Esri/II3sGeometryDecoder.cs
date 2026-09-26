namespace AskLucy.Infrastructure.Buildings.Esri;

/// <summary>
/// Turns one node's compressed mesh into where each of its features (buildings) sits. An interface
/// so the traversal and height rules can be tested without real Draco bytes.
/// </summary>
internal interface II3sGeometryDecoder
{
    /// <summary>
    /// The centre of each feature's footprint, indexed like the node's attribute arrays; null for a
    /// feature with no vertices. Throws <see cref="InvalidDataException"/> for unreadable geometry.
    /// </summary>
    IReadOnlyList<(double Longitude, double Latitude)?> DecodeFeatureCentres(
        byte[] geometry, double nodeCenterLongitude, double nodeCenterLatitude, int featureCount);
}
