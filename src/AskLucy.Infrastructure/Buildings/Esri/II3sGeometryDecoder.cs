namespace AskLucy.Infrastructure.Buildings.Esri;

/// <summary>
/// Turns one node's compressed mesh into geographic vertices, their feature (building) and the
/// mesh's triangles. An interface so the traversal and height rules can be tested without real
/// Draco bytes.
/// </summary>
internal interface II3sGeometryDecoder
{
    /// <summary>Throws <see cref="InvalidDataException"/> for unreadable geometry.</summary>
    I3sMesh Decode(byte[] geometry, double nodeCenterLongitude, double nodeCenterLatitude);
}

/// <summary>
/// One node's mesh. Vertex <c>i</c> is at (<see cref="Longitudes"/>[i], <see cref="Latitudes"/>[i])
/// with elevation <see cref="Elevations"/>[i] in metres (relative to the node, so only differences
/// within a feature mean anything) and belongs to feature <see cref="Features"/>[i], which indexes
/// the node's attribute arrays. <see cref="Triangles"/> holds three vertex indices per triangle.
/// </summary>
internal sealed record I3sMesh(double[] Longitudes, double[] Latitudes, float[] Elevations, int[] Features, int[] Triangles)
{
    public int VertexCount => Features.Length;
}
