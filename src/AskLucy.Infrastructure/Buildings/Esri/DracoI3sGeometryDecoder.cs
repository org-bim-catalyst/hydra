using System.Buffers.Binary;
using System.Text;
using Openize.Drako;

namespace AskLucy.Infrastructure.Buildings.Esri;

/// <summary>
/// Decodes the Draco-compressed geometry of an I3S mesh node. Each vertex position is an offset
/// from the node's centre in degrees, stored pre-divided: longitude offset = x * <c>i3s-scale_x</c>,
/// latitude offset = y * <c>i3s-scale_y</c>. Those scales live in the Draco file's metadata, which
/// Openize.Drako does not expose, so <see cref="ReadPositionScales"/> reads them from the header.
/// A second (generic) attribute gives each vertex's feature index.
/// </summary>
/// <remarks>
/// specs/075 research — verified against OpenStreetMap's own coordinates for the same OSM ids:
/// treating the offsets as metres put buildings up to 75 m off, while the metadata scales place
/// them to within millimetres.
/// </remarks>
internal sealed class DracoI3sGeometryDecoder : II3sGeometryDecoder
{
    public I3sMesh Decode(byte[] geometry, double nodeCenterLongitude, double nodeCenterLatitude)
    {
        var (scaleX, scaleY) = ReadPositionScales(geometry);
        var mesh = Draco.Decode(geometry) as DracoMesh
            ?? throw new InvalidDataException("The node geometry is not a Draco mesh.");
        var positions = mesh.GetNamedAttribute(AttributeType.Position, 0)
            ?? throw new InvalidDataException("The node geometry has no positions.");
        var featureIndices = mesh.GetNamedAttribute(AttributeType.Generic, 0)
            ?? throw new InvalidDataException("The node geometry has no feature index.");

        var longitudes = new double[mesh.NumPoints];
        var latitudes = new double[mesh.NumPoints];
        var elevations = new float[mesh.NumPoints];
        var feature = new int[mesh.NumPoints];
        var buffer = new byte[Math.Max(featureIndices.ByteStride, 8)];
        for (var point = 0; point < mesh.NumPoints; point++)
        {
            var position = positions.GetValueAsVector3(positions.MappedIndex(point));
            longitudes[point] = nodeCenterLongitude + (position.X * scaleX);
            latitudes[point] = nodeCenterLatitude + (position.Y * scaleY);
            elevations[point] = position.Z;
            featureIndices.GetValue(featureIndices.MappedIndex(point), buffer);
            feature[point] = featureIndices.ByteStride switch
            {
                1 => buffer[0],
                2 => BinaryPrimitives.ReadUInt16LittleEndian(buffer),
                _ => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(buffer)),
            };
        }

        var triangles = new int[mesh.NumFaces * 3];
        Span<int> face = stackalloc int[3];
        for (var f = 0; f < mesh.NumFaces; f++)
        {
            mesh.ReadFace(f, face);
            for (var corner = 0; corner < 3; corner++)
            {
                if ((uint)face[corner] >= (uint)mesh.NumPoints)
                    throw new InvalidDataException($"Triangle {f} of the node geometry points past its vertices.");
                triangles[(f * 3) + corner] = face[corner];
            }
        }

        return new I3sMesh(longitudes, latitudes, elevations, feature, triangles);
    }

    /// <summary>
    /// Reads <c>i3s-scale_x</c> and <c>i3s-scale_y</c> (Float64) from the metadata that follows the
    /// Draco header ("DRACO", major, minor, encoder, method, UInt16 flags) when the metadata flag is
    /// set: a varint count of attribute metadata, then per attribute a varint id, a varint entry
    /// count, and per entry a byte-length-prefixed name and value.
    /// </summary>
    internal static (double ScaleX, double ScaleY) ReadPositionScales(byte[] geometry)
    {
        const ushort MetadataFlag = 0x8000;
        if (geometry.Length < 11 || Encoding.ASCII.GetString(geometry, 0, 5) != "DRACO")
            throw new InvalidDataException("The node geometry is not a Draco file.");
        if ((BinaryPrimitives.ReadUInt16LittleEndian(geometry.AsSpan(9)) & MetadataFlag) == 0)
            throw new InvalidDataException("The node geometry carries no I3S position scales.");

        var position = 11;
        try
        {
            int ReadVarint()
            {
                int result = 0, shift = 0;
                while (true)
                {
                    int b = geometry[position++];
                    result |= (b & 0x7F) << shift;
                    if (b < 0x80) return result;
                    shift += 7;
                }
            }

            double? scaleX = null, scaleY = null;
            var attributeMetadataCount = ReadVarint();
            for (var a = 0; a < attributeMetadataCount && (scaleX is null || scaleY is null); a++)
            {
                ReadVarint(); // the attribute's unique id
                var entryCount = ReadVarint();
                for (var e = 0; e < entryCount; e++)
                {
                    int nameLength = geometry[position++];
                    var name = Encoding.ASCII.GetString(geometry, position, nameLength);
                    position += nameLength;
                    int valueLength = geometry[position++];
                    if (valueLength == 8 && name == "i3s-scale_x") scaleX = BinaryPrimitives.ReadDoubleLittleEndian(geometry.AsSpan(position));
                    if (valueLength == 8 && name == "i3s-scale_y") scaleY = BinaryPrimitives.ReadDoubleLittleEndian(geometry.AsSpan(position));
                    position += valueLength;
                }

                // I3S writes no nested metadata; any means this isn't the layout above.
                if (ReadVarint() != 0) break;
            }

            return scaleX is { } sx && scaleY is { } sy
                ? (sx, sy)
                : throw new InvalidDataException("The node geometry carries no I3S position scales.");
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException)
        {
            throw new InvalidDataException("The node geometry's metadata is truncated.", ex);
        }
    }
}
