using System.Buffers.Binary;

namespace AskLucy.Application.Ai.Dictation;

/// <summary>
/// specs/078 FR-013 — reads a dictation clip's RIFF/WAVE header. Only 16 kHz mono 16-bit PCM is
/// accepted: the format both Local Whisper and OpenAI Whisper take, and the one the browser's
/// encoder writes. The declared content type is never trusted.
/// </summary>
public sealed record WavHeader(int SampleRate, int Channels, int BitsPerSample, long DataLength)
{
    public const int RequiredSampleRate = 16000;
    public const int RequiredChannels = 1;
    public const int RequiredBitsPerSample = 16;

    private const ushort PcmFormat = 1;
    private const int MaxChunksBeforeData = 16;

    public TimeSpan Duration => TimeSpan.FromSeconds((double)DataLength / (SampleRate * Channels * (BitsPerSample / 8)));

    /// <summary>
    /// Reads the header from the stream's current position and puts the position back. Returns
    /// false with a reason when the stream isn't a clip in the required format.
    /// </summary>
    public static bool TryRead(Stream stream, out WavHeader? header, out string? error)
    {
        ArgumentNullException.ThrowIfNull(stream);
        header = null;

        if (!stream.CanSeek)
        {
            error = "The audio stream must be seekable.";
            return false;
        }

        var start = stream.Position;
        try
        {
            return TryReadCore(stream, out header, out error);
        }
        finally
        {
            stream.Position = start;
        }
    }

    private static bool TryReadCore(Stream stream, out WavHeader? header, out string? error)
    {
        header = null;
        Span<byte> riff = stackalloc byte[12];
        if (!ReadExactly(stream, riff) || !Is(riff[..4], "RIFF"u8) || !Is(riff[8..12], "WAVE"u8))
        {
            error = "The audio isn't a WAV file.";
            return false;
        }

        (int SampleRate, int Channels, int Bits)? format = null;
        Span<byte> chunk = stackalloc byte[8];
        Span<byte> fmt = stackalloc byte[16];
        for (var i = 0; i < MaxChunksBeforeData; i++)
        {
            if (!ReadExactly(stream, chunk))
            {
                break;
            }

            var size = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
            if (Is(chunk[..4], "fmt "u8))
            {
                if (size < 16)
                {
                    break;
                }

                if (!ReadExactly(stream, fmt))
                {
                    break;
                }

                if (BinaryPrimitives.ReadUInt16LittleEndian(fmt) != PcmFormat)
                {
                    error = "The WAV audio must be uncompressed PCM.";
                    return false;
                }

                format = (
                    (int)BinaryPrimitives.ReadUInt32LittleEndian(fmt[4..]),
                    BinaryPrimitives.ReadUInt16LittleEndian(fmt[2..]),
                    BinaryPrimitives.ReadUInt16LittleEndian(fmt[14..]));
                Skip(stream, size - 16 + (size % 2));
                continue;
            }

            if (Is(chunk[..4], "data"u8))
            {
                if (format is not { } f)
                {
                    break;
                }

                if (f.SampleRate != RequiredSampleRate || f.Channels != RequiredChannels || f.Bits != RequiredBitsPerSample)
                {
                    error = $"The WAV audio must be {RequiredSampleRate} Hz, mono, {RequiredBitsPerSample}-bit.";
                    return false;
                }

                if (size == 0)
                {
                    error = "The WAV audio is empty.";
                    return false;
                }

                header = new WavHeader(f.SampleRate, f.Channels, f.Bits, size);
                error = null;
                return true;
            }

            Skip(stream, size + (size % 2));
        }

        error = "The WAV header is incomplete.";
        return false;
    }

    private static bool Is(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> tag) => bytes.SequenceEqual(tag);

    private static bool ReadExactly(Stream stream, Span<byte> buffer) =>
        stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false) == buffer.Length;

    private static void Skip(Stream stream, long count) => stream.Seek(count, SeekOrigin.Current);
}
