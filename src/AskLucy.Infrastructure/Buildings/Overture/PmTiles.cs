using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace AskLucy.Infrastructure.Buildings.Overture;

/// <summary>
/// The fixed 127-byte header of a PMTiles v3 archive: where the root directory, the leaf
/// directories and the tile data live, and how they are compressed.
/// </summary>
internal sealed record PmTilesHeader(
    long RootDirectoryOffset,
    long RootDirectoryLength,
    long LeafDirectoriesOffset,
    long TileDataOffset,
    PmTilesCompression InternalCompression,
    PmTilesCompression TileCompression,
    byte TileType,
    int MinZoom,
    int MaxZoom)
{
    public const int Length = 127;
    public const byte MapboxVectorTileType = 1;

    public static PmTilesHeader Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < Length || Encoding.ASCII.GetString(bytes[..7]) != "PMTiles" || bytes[7] != 3)
            throw new InvalidDataException("Not a PMTiles v3 archive.");

        static long At(ReadOnlySpan<byte> header, int field) =>
            checked((long)BinaryPrimitives.ReadUInt64LittleEndian(header[(8 + (8 * field))..]));
        return new PmTilesHeader(
            RootDirectoryOffset: At(bytes, 0),
            RootDirectoryLength: At(bytes, 1),
            LeafDirectoriesOffset: At(bytes, 4),
            TileDataOffset: At(bytes, 6),
            InternalCompression: (PmTilesCompression)bytes[97],
            TileCompression: (PmTilesCompression)bytes[98],
            TileType: bytes[99],
            MinZoom: bytes[100],
            MaxZoom: bytes[101]);
    }
}

internal enum PmTilesCompression : byte
{
    Unknown = 0,
    None = 1,
    Gzip = 2,
}

/// <summary>
/// One directory entry. A run length of 0 means the entry points at a leaf directory (relative
/// to the leaf-directories section) rather than at tile data.
/// </summary>
internal readonly record struct PmTilesEntry(ulong TileId, long Offset, int Length, int RunLength);

internal static class PmTiles
{
    /// <summary>
    /// A tile's position on the archive-wide Hilbert curve: every tile of lower zooms first
    /// (4^0 + … + 4^(z-1)), then the tile's index along its own zoom's Hilbert curve.
    /// </summary>
    public static ulong TileId(int zoom, int x, int y)
    {
        ulong accumulated = 0;
        for (var z = 0; z < zoom; z++) accumulated += 1UL << (2 * z);

        ulong d = 0;
        long tx = x, ty = y;
        for (long s = 1L << (zoom - 1); zoom > 0 && s > 0; s /= 2)
        {
            long rx = (tx & s) > 0 ? 1 : 0;
            long ry = (ty & s) > 0 ? 1 : 0;
            d += (ulong)(s * s * ((3 * rx) ^ ry));
            if (ry == 0)
            {
                if (rx == 1)
                {
                    tx = s - 1 - tx;
                    ty = s - 1 - ty;
                }

                (tx, ty) = (ty, tx);
            }
        }

        return accumulated + d;
    }

    /// <summary>
    /// Decodes a (decompressed) directory: an entry count, then column by column the delta-encoded
    /// tile ids, the run lengths, the lengths, and the offsets — where an offset of 0 means
    /// "straight after the previous entry" and anything else is the offset plus one.
    /// </summary>
    public static IReadOnlyList<PmTilesEntry> ParseDirectory(ReadOnlySpan<byte> bytes)
    {
        var position = 0;
        ulong ReadVarint(ReadOnlySpan<byte> b)
        {
            ulong result = 0;
            for (var shift = 0; ; shift += 7)
            {
                if (position >= b.Length || shift > 63) throw new InvalidDataException("Truncated PMTiles directory.");
                var value = b[position++];
                result |= (ulong)(value & 0x7F) << shift;
                if (value < 0x80) return result;
            }
        }

        var count = checked((int)ReadVarint(bytes));
        var ids = new ulong[count];
        var runLengths = new int[count];
        var lengths = new int[count];
        var offsets = new long[count];

        ulong lastId = 0;
        for (var i = 0; i < count; i++) ids[i] = lastId += ReadVarint(bytes);
        for (var i = 0; i < count; i++) runLengths[i] = checked((int)ReadVarint(bytes));
        for (var i = 0; i < count; i++) lengths[i] = checked((int)ReadVarint(bytes));
        for (var i = 0; i < count; i++)
        {
            var value = ReadVarint(bytes);
            offsets[i] = value == 0 && i > 0 ? offsets[i - 1] + lengths[i - 1] : checked((long)value - 1);
        }

        var entries = new PmTilesEntry[count];
        for (var i = 0; i < count; i++) entries[i] = new PmTilesEntry(ids[i], offsets[i], lengths[i], runLengths[i]);
        return entries;
    }

    /// <summary>
    /// The entry covering <paramref name="tileId"/>: the last entry at or before it, provided it is
    /// either a leaf-directory pointer or a run that reaches the tile. Null when the archive has no
    /// such tile (open sea, empty desert).
    /// </summary>
    public static PmTilesEntry? Find(IReadOnlyList<PmTilesEntry> entries, ulong tileId)
    {
        int low = 0, high = entries.Count - 1, found = -1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            if (entries[middle].TileId <= tileId)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        if (found < 0) return null;
        var entry = entries[found];
        if (entry.RunLength == 0) return entry;
        return tileId < entry.TileId + (ulong)entry.RunLength ? entry : null;
    }

    public static byte[] Decompress(byte[] bytes, PmTilesCompression compression) => compression switch
    {
        PmTilesCompression.None => bytes,
        PmTilesCompression.Gzip => Gunzip(bytes),
        _ => throw new InvalidDataException($"PMTiles compression {compression} is not supported."),
    };

    private static byte[] Gunzip(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress))
        {
            gzip.CopyTo(output);
        }

        return output.ToArray();
    }
}
