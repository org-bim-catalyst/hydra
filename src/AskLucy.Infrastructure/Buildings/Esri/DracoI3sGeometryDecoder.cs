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
    public IReadOnlyList<(double Longitude, double Latitude)?> DecodeFeatureCentres(
        byte[] geometry, double nodeCenterLongitude, double nodeCenterLatitude, int featureCount)
    {
        var (scaleX, scaleY) = ReadPositionScales(geometry);
        var mesh = Draco.Decode(geometry)
            ?? throw new InvalidDataException("The node geometry is not a Draco mesh.");
        var positions = mesh.GetNamedAttribute(AttributeType.Position, 0)
            ?? throw new InvalidDataException("The node geometry has no positions.");
        var featureIndices = mesh.GetNamedAttribute(AttributeType.Generic, 0)
            ?? throw new InvalidDataException("The node geometry has no feature index.");

        var x = new float[mesh.NumPoints];
        var y = new float[mesh.NumPoints];
        var feature = new int[mesh.NumPoints];
        var buffer = new byte[Math.Max(featureIndices.ByteStride, 8)];
        for (var point = 0; point < mesh.NumPoints; point++)
        {
            var position = positions.GetValueAsVector3(positions.MappedIndex(point));
            x[point] = position.X;
            y[point] = position.Y;
            featureIndices.GetValue(featureIndices.MappedIndex(point), buffer);
            feature[point] = featureIndices.ByteStride switch
            {
                1 => buffer[0],
                2 => BinaryPrimitives.ReadUInt16LittleEndian(buffer),
                _ => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(buffer)),
            };
        }

        return FeatureCentres(x, y, feature, featureCount)
            .Select(c => c is { } offset
                ? ((double Longitude, double Latitude)?)(nodeCenterLongitude + (offset.X * scaleX), nodeCenterLatitude + (offset.Y * scaleY))
                : null)
            .ToList();
    }

    /// <summary>The centre of each feature's bounding box, in the geometry's own units.</summary>
    internal static (double X, double Y)?[] FeatureCentres(float[] x, float[] y, int[] feature, int featureCount)
    {
        var minX = Enumerable.Repeat(double.MaxValue, featureCount).ToArray();
        var minY = Enumerable.Repeat(double.MaxValue, featureCount).ToArray();
        var maxX = Enumerable.Repeat(double.MinValue, featureCount).ToArray();
        var maxY = Enumerable.Repeat(double.MinValue, featureCount).ToArray();

        for (var i = 0; i < feature.Length; i++)
        {
            var f = feature[i];
            if (f < 0 || f >= featureCount) continue;
            minX[f] = Math.Min(minX[f], x[i]);
            maxX[f] = Math.Max(maxX[f], x[i]);
            minY[f] = Math.Min(minY[f], y[i]);
            maxY[f] = Math.Max(maxY[f], y[i]);
        }

        var centres = new (double X, double Y)?[featureCount];
        for (var f = 0; f < featureCount; f++)
        {
            if (minX[f] <= maxX[f])
            {
                centres[f] = ((minX[f] + maxX[f]) / 2, (minY[f] + maxY[f]) / 2);
            }
        }

        return centres;
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
