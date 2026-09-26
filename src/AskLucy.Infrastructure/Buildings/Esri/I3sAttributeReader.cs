using System.Buffers.Binary;
using System.Text;

namespace AskLucy.Infrastructure.Buildings.Esri;

/// <summary>
/// Reads an I3S binary attribute buffer (one per attribute per node). Numbers: a UInt32 count,
/// then the values, starting at the first offset aligned to their own size, so Float64 values skip
/// four bytes of padding after the count. Strings: a UInt32 count and a UInt32 total byte count,
/// then one UInt32 byte length per value, then the UTF-8 values, each null-terminated.
/// </summary>
internal static class I3sAttributeReader
{
    public static double[] ReadNumbers(ReadOnlySpan<byte> data, string valueType)
    {
        var count = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data));
        var size = valueType switch
        {
            "Float64" => 8,
            "Float32" or "Int32" or "UInt32" => 4,
            _ => throw new InvalidDataException($"I3S attribute value type '{valueType}' is not supported."),
        };
        var offset = Math.Max(4, size);
        if (data.Length < offset + (count * size))
            throw new InvalidDataException("The I3S attribute buffer is shorter than its count says.");

        var values = new double[count];
        for (var i = 0; i < count; i++)
        {
            var slice = data.Slice(offset + (i * size), size);
            values[i] = valueType switch
            {
                "Float64" => BinaryPrimitives.ReadDoubleLittleEndian(slice),
                "Float32" => BinaryPrimitives.ReadSingleLittleEndian(slice),
                "Int32" => BinaryPrimitives.ReadInt32LittleEndian(slice),
                _ => BinaryPrimitives.ReadUInt32LittleEndian(slice),
            };
        }

        return values;
    }

    public static string[] ReadStrings(ReadOnlySpan<byte> data)
    {
        var count = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data));
        const int LengthsOffset = 8;
        var valueOffset = LengthsOffset + (4 * count);

        var values = new string[count];
        for (var i = 0; i < count; i++)
        {
            var length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data[(LengthsOffset + (4 * i))..]));
            if (data.Length < valueOffset + length)
                throw new InvalidDataException("The I3S attribute buffer is shorter than its lengths say.");
            values[i] = Encoding.UTF8.GetString(data.Slice(valueOffset, length)).TrimEnd('\0', ' ');
            valueOffset += length;
        }

        return values;
    }
}
