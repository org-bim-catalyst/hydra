using System.Buffers.Binary;
using System.Text;

namespace AskLucy.Infrastructure.Buildings.Overture;

/// <summary>
/// A minimal Mapbox Vector Tile (v2) reader: only what building footprints need. Polygons come
/// back as rings of tile coordinates (0..extent, y down); property values as string, double,
/// long or bool.
/// </summary>
internal static class MvtTile
{
    private const int PolygonGeometryType = 3;

    public static IReadOnlyList<MvtLayer> Parse(ReadOnlySpan<byte> tile)
    {
        var layers = new List<MvtLayer>();
        var reader = new ProtobufReader(tile);
        while (reader.Next(out var field, out var wireType))
        {
            if (field == 3 && wireType == 2) layers.Add(ParseLayer(reader.ReadBytes()));
            else reader.Skip(wireType);
        }

        return layers;
    }

    private static MvtLayer ParseLayer(ReadOnlySpan<byte> bytes)
    {
        var name = string.Empty;
        var extent = 4096;
        var keys = new List<string>();
        var values = new List<object?>();
        var featureOffsets = new List<(int Start, int Length)>();

        var reader = new ProtobufReader(bytes);
        while (reader.Next(out var field, out var wireType))
        {
            switch (field, wireType)
            {
                case (1, 2): name = Encoding.UTF8.GetString(reader.ReadBytes()); break;
                case (2, 2): featureOffsets.Add(reader.ReadBytesRange()); break;
                case (3, 2): keys.Add(Encoding.UTF8.GetString(reader.ReadBytes())); break;
                case (4, 2): values.Add(ParseValue(reader.ReadBytes())); break;
                case (5, 0): extent = checked((int)reader.ReadVarint()); break;
                default: reader.Skip(wireType); break;
            }
        }

        var features = new List<MvtFeature>(featureOffsets.Count);
        foreach (var (start, length) in featureOffsets)
        {
            if (ParseFeature(bytes.Slice(start, length), keys, values) is { } feature) features.Add(feature);
        }

        return new MvtLayer(name, extent, features);
    }

    /// <summary>Null for anything but a polygon.</summary>
    private static MvtFeature? ParseFeature(ReadOnlySpan<byte> bytes, List<string> keys, List<object?> values)
    {
        var properties = new Dictionary<string, object?>();
        var geometryType = 0;
        List<uint>? commands = null;

        var reader = new ProtobufReader(bytes);
        while (reader.Next(out var field, out var wireType))
        {
            switch (field, wireType)
            {
                case (2, 2):
                    var tags = ReadPacked(reader.ReadBytes());
                    for (var i = 0; i + 1 < tags.Count; i += 2)
                    {
                        if (tags[i] < keys.Count && tags[i + 1] < values.Count)
                            properties[keys[(int)tags[i]]] = values[(int)tags[i + 1]];
                    }

                    break;
                case (3, 0): geometryType = checked((int)reader.ReadVarint()); break;
                case (4, 2): commands = ReadPacked(reader.ReadBytes()); break;
                default: reader.Skip(wireType); break;
            }
        }

        return geometryType == PolygonGeometryType && commands is not null
            ? new MvtFeature(properties, DecodeRings(commands))
            : null;
    }

    /// <summary>MoveTo (1), LineTo (2) and ClosePath (7) commands with zig-zag-encoded deltas.</summary>
    internal static IReadOnlyList<MvtRing> DecodeRings(IReadOnlyList<uint> commands)
    {
        var rings = new List<MvtRing>();
        List<(double X, double Y)>? current = null;
        long x = 0, y = 0;
        var i = 0;
        while (i < commands.Count)
        {
            var command = commands[i] & 0x7;
            var count = (int)(commands[i] >> 3);
            i++;
            if (command == 7)
            {
                if (current is { Count: >= 3 }) rings.Add(new MvtRing(current));
                current = null;
                continue;
            }

            for (var n = 0; n < count && i + 1 < commands.Count; n++, i += 2)
            {
                x += ZigZag(commands[i]);
                y += ZigZag(commands[i + 1]);
                if (command == 1) current = [];
                current?.Add((x, y));
            }
        }

        return rings;
    }

    private static long ZigZag(uint value) => (value >> 1) ^ -(long)(value & 1);

    private static List<uint> ReadPacked(ReadOnlySpan<byte> bytes)
    {
        var result = new List<uint>();
        var reader = new ProtobufReader(bytes);
        while (!reader.AtEnd) result.Add(checked((uint)reader.ReadVarint()));
        return result;
    }

    private static object? ParseValue(ReadOnlySpan<byte> bytes)
    {
        var reader = new ProtobufReader(bytes);
        while (reader.Next(out var field, out var wireType))
        {
            switch (field, wireType)
            {
                case (1, 2): return Encoding.UTF8.GetString(reader.ReadBytes());
                case (2, 5): return (double)BinaryPrimitives.ReadSingleLittleEndian(reader.ReadFixed(4));
                case (3, 1): return BinaryPrimitives.ReadDoubleLittleEndian(reader.ReadFixed(8));
                case (4, 0): return (long)reader.ReadVarint();
                case (5, 0): return (long)reader.ReadVarint();
                case (6, 0):
                    var zigzag = reader.ReadVarint();
                    return (long)(zigzag >> 1) ^ -(long)(zigzag & 1);
                case (7, 0): return reader.ReadVarint() != 0;
                default: reader.Skip(wireType); break;
            }
        }

        return null;
    }

    private ref struct ProtobufReader(ReadOnlySpan<byte> bytes)
    {
        private readonly ReadOnlySpan<byte> _bytes = bytes;
        private int _position;

        public readonly bool AtEnd => _position >= _bytes.Length;

        public bool Next(out int field, out int wireType)
        {
            if (AtEnd)
            {
                field = wireType = 0;
                return false;
            }

            var key = ReadVarint();
            field = (int)(key >> 3);
            wireType = (int)(key & 7);
            return true;
        }

        public ulong ReadVarint()
        {
            ulong result = 0;
            for (var shift = 0; ; shift += 7)
            {
                if (AtEnd || shift > 63) throw new InvalidDataException("Truncated vector tile.");
                var value = _bytes[_position++];
                result |= (ulong)(value & 0x7F) << shift;
                if (value < 0x80) return result;
            }
        }

        public ReadOnlySpan<byte> ReadBytes()
        {
            var (start, length) = ReadBytesRange();
            return _bytes.Slice(start, length);
        }

        public (int Start, int Length) ReadBytesRange()
        {
            var length = checked((int)ReadVarint());
            if (_position + length > _bytes.Length) throw new InvalidDataException("Truncated vector tile.");
            var start = _position;
            _position += length;
            return (start, length);
        }

        public ReadOnlySpan<byte> ReadFixed(int length)
        {
            if (_position + length > _bytes.Length) throw new InvalidDataException("Truncated vector tile.");
            var slice = _bytes.Slice(_position, length);
            _position += length;
            return slice;
        }

        public void Skip(int wireType)
        {
            switch (wireType)
            {
                case 0: ReadVarint(); break;
                case 1: ReadFixed(8); break;
                case 2: ReadBytesRange(); break;
                case 5: ReadFixed(4); break;
                default: throw new InvalidDataException($"Unsupported protobuf wire type {wireType}.");
            }
        }
    }
}

internal sealed record MvtLayer(string Name, int Extent, IReadOnlyList<MvtFeature> Features);

internal sealed record MvtFeature(IReadOnlyDictionary<string, object?> Properties, IReadOnlyList<MvtRing> Rings);

/// <summary>A closed ring in tile coordinates (the closing point is implied, not repeated).</summary>
internal sealed record MvtRing(IReadOnlyList<(double X, double Y)> Points)
{
    /// <summary>
    /// Positive for an exterior ring, negative for a hole: the MVT spec fixes the winding so the
    /// surveyor's formula in tile coordinates (y down) gives exteriors a positive area.
    /// </summary>
    public double SignedArea()
    {
        double sum = 0;
        for (int i = 0, j = Points.Count - 1; i < Points.Count; j = i++)
        {
            sum += (Points[j].X * Points[i].Y) - (Points[i].X * Points[j].Y);
        }

        return sum / 2;
    }
}
