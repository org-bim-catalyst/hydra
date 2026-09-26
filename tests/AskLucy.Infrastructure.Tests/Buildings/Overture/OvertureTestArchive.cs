using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Text;
using AskLucy.Infrastructure.Buildings.Overture;

namespace AskLucy.Infrastructure.Tests.Buildings.Overture;

/// <summary>A building polygon for <see cref="VectorTileWriter"/>, in tile coordinates (y down).</summary>
internal sealed record TestBuilding(
    IReadOnlyList<IReadOnlyList<(int X, int Y)>> Rings,
    IReadOnlyDictionary<string, object> Properties);

/// <summary>Encodes just enough of the Mapbox Vector Tile format for the reader under test.</summary>
internal static class VectorTileWriter
{
    public static byte[] Tile(params (string Name, IReadOnlyList<TestBuilding> Features)[] layers)
    {
        var tile = new ProtoWriter();
        foreach (var (name, features) in layers) tile.Bytes(3, Layer(name, features));
        return tile.ToArray();
    }

    private static byte[] Layer(string name, IReadOnlyList<TestBuilding> features)
    {
        var keys = new List<string>();
        var values = new List<object>();
        var layer = new ProtoWriter();
        layer.Varint(15, 2);
        layer.Bytes(1, Encoding.UTF8.GetBytes(name));
        foreach (var feature in features)
        {
            var tags = new List<uint>();
            foreach (var (key, value) in feature.Properties)
            {
                if (!keys.Contains(key)) keys.Add(key);
                if (!values.Contains(value)) values.Add(value);
                tags.Add((uint)keys.IndexOf(key));
                tags.Add((uint)values.IndexOf(value));
            }

            var encoded = new ProtoWriter();
            encoded.Packed(2, tags);
            encoded.Varint(3, 3);
            encoded.Packed(4, Geometry(feature.Rings));
            layer.Bytes(2, encoded.ToArray());
        }

        foreach (var key in keys) layer.Bytes(3, Encoding.UTF8.GetBytes(key));
        foreach (var value in values) layer.Bytes(4, Value(value));
        layer.Varint(5, 4096);
        return layer.ToArray();
    }

    private static List<uint> Geometry(IReadOnlyList<IReadOnlyList<(int X, int Y)>> rings)
    {
        var commands = new List<uint>();
        int cursorX = 0, cursorY = 0;
        foreach (var ring in rings)
        {
            for (var i = 0; i < ring.Count; i++)
            {
                if (i == 0) commands.Add(1 | (1 << 3));
                if (i == 1) commands.Add((uint)(2 | ((ring.Count - 1) << 3)));
                commands.Add(ZigZag(ring[i].X - cursorX));
                commands.Add(ZigZag(ring[i].Y - cursorY));
                (cursorX, cursorY) = ring[i];
            }

            commands.Add(7 | (1 << 3));
        }

        return commands;
    }

    private static uint ZigZag(int value) => (uint)((value << 1) ^ (value >> 31));

    private static byte[] Value(object value)
    {
        var writer = new ProtoWriter();
        switch (value)
        {
            case string s: writer.Bytes(1, Encoding.UTF8.GetBytes(s)); break;
            case double d: writer.Fixed64(3, d); break;
            case int i: writer.Varint(4, (ulong)i); break;
            case bool b: writer.Varint(7, b ? 1UL : 0UL); break;
            default: throw new ArgumentException($"Unsupported value {value}.");
        }

        return writer.ToArray();
    }

    private sealed class ProtoWriter
    {
        private readonly List<byte> _bytes = [];

        public void Varint(int field, ulong value)
        {
            Raw(((ulong)field << 3) | 0);
            Raw(value);
        }

        public void Bytes(int field, byte[] bytes)
        {
            Raw(((ulong)field << 3) | 2);
            Raw((ulong)bytes.Length);
            _bytes.AddRange(bytes);
        }

        public void Fixed64(int field, double value)
        {
            Raw(((ulong)field << 3) | 1);
            var buffer = new byte[8];
            BinaryPrimitives.WriteDoubleLittleEndian(buffer, value);
            _bytes.AddRange(buffer);
        }

        public void Packed(int field, IEnumerable<uint> values)
        {
            var packed = new ProtoWriter();
            foreach (var value in values) packed.Raw(value);
            Bytes(field, packed.ToArray());
        }

        public byte[] ToArray() => [.. _bytes];

        private void Raw(ulong value)
        {
            while (value >= 0x80)
            {
                _bytes.Add((byte)(value | 0x80));
                value >>= 7;
            }

            _bytes.Add((byte)value);
        }
    }
}

/// <summary>
/// Builds a PMTiles v3 archive (gzip directories and tiles, MVT) and serves it — plus an S3
/// release listing — the way the Overture bucket does, including byte-range requests.
/// </summary>
internal sealed class OvertureTestBucket
{
    public const string BucketUrl = "https://bucket.test/";

    private readonly Dictionary<string, byte[]> _archives = new();

    public List<string> Requests { get; } = [];

    public List<string> ListedReleases { get; } = [];

    public HttpStatusCode? FailWith { get; set; }

    public void AddRelease(string release, IReadOnlyDictionary<ulong, byte[]> tiles, bool useLeafDirectory = false)
    {
        ListedReleases.Add(release);
        _archives[$"{BucketUrl}tiles/{release}/buildings.pmtiles"] = Build(tiles, useLeafDirectory);
    }

    /// <summary>A listed release whose archive is missing, like one still uploading.</summary>
    public void AddEmptyRelease(string release) => ListedReleases.Add(release);

    public HttpResponseMessage Respond(HttpRequestMessage request)
    {
        var url = request.RequestUri!.ToString();
        lock (Requests) Requests.Add(url);
        if (FailWith is { } status) return new HttpResponseMessage(status);

        if (url.Contains("list-type=2", StringComparison.Ordinal))
        {
            var prefixes = string.Concat(ListedReleases.Select(r => $"<CommonPrefixes><Prefix>tiles/{r}/</Prefix></CommonPrefixes>"));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $"""<?xml version="1.0" encoding="UTF-8"?><ListBucketResult xmlns="http://s3.amazonaws.com/doc/2006-03-01/"><Prefix>tiles/</Prefix>{prefixes}</ListBucketResult>"""),
            };
        }

        if (!_archives.TryGetValue(url, out var archive)) return new HttpResponseMessage(HttpStatusCode.NotFound);

        var range = request.Headers.Range!.Ranges.Single();
        var from = (int)range.From!.Value;
        var to = (int)range.To!.Value;
        return new HttpResponseMessage(HttpStatusCode.PartialContent)
        {
            Content = new ByteArrayContent(archive[from..(to + 1)]),
        };
    }

    private static byte[] Build(IReadOnlyDictionary<ulong, byte[]> tiles, bool useLeafDirectory)
    {
        var data = new MemoryStream();
        var entries = new List<PmTilesEntry>();
        foreach (var (id, tile) in tiles.OrderBy(t => t.Key))
        {
            var compressed = Gzip(tile);
            entries.Add(new PmTilesEntry(id, data.Length, compressed.Length, 1));
            data.Write(compressed);
        }

        var leaves = Array.Empty<byte>();
        byte[] root;
        if (useLeafDirectory)
        {
            leaves = Gzip(Directory(entries));
            root = Gzip(Directory([new PmTilesEntry(entries[0].TileId, 0, leaves.Length, 0)]));
        }
        else
        {
            root = Gzip(Directory(entries));
        }

        var header = new byte[PmTilesHeader.Length];
        Encoding.ASCII.GetBytes("PMTiles").CopyTo(header, 0);
        header[7] = 3;
        long rootOffset = PmTilesHeader.Length, leafOffset = rootOffset + root.Length, dataOffset = leafOffset + leaves.Length;
        long[] fields = [rootOffset, root.Length, 0, 0, leafOffset, leaves.Length, dataOffset, data.Length];
        for (var i = 0; i < fields.Length; i++) BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(8 + (8 * i)), (ulong)fields[i]);
        header[97] = 2;
        header[98] = 2;
        header[99] = 1;
        header[100] = 0;
        header[101] = 14;

        return [.. header, .. root, .. leaves, .. data.ToArray()];
    }

    /// <summary>Uses the "0 = straight after the previous entry" offset form wherever it applies.</summary>
    internal static byte[] Directory(IReadOnlyList<PmTilesEntry> entries)
    {
        var stream = new MemoryStream();
        void Varint(ulong value)
        {
            while (value >= 0x80)
            {
                stream.WriteByte((byte)(value | 0x80));
                value >>= 7;
            }

            stream.WriteByte((byte)value);
        }

        Varint((ulong)entries.Count);
        ulong lastId = 0;
        foreach (var e in entries)
        {
            Varint(e.TileId - lastId);
            lastId = e.TileId;
        }

        foreach (var e in entries) Varint((ulong)e.RunLength);
        foreach (var e in entries) Varint((ulong)e.Length);
        for (var i = 0; i < entries.Count; i++)
        {
            var contiguous = i > 0 && entries[i].Offset == entries[i - 1].Offset + entries[i - 1].Length;
            Varint(contiguous ? 0 : (ulong)entries[i].Offset + 1);
        }

        return stream.ToArray();
    }

    private static byte[] Gzip(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(bytes);
        }

        return output.ToArray();
    }
}
