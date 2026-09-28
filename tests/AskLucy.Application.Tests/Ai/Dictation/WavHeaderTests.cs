using System.Buffers.Binary;
using System.Text;
using AskLucy.Application.Ai.Dictation;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Ai.Dictation;

/// <summary>specs/078 FR-013 — only 16 kHz mono 16-bit PCM WAV reaches a clip engine.</summary>
public sealed class WavHeaderTests
{
    private static MemoryStream Wav(int sampleRate = 16000, short channels = 1, short bits = 16, short format = 1, int dataBytes = 3200, bool extraChunk = false)
    {
        var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write("RIFF"u8);
        writer.Write(0);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write(format);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bits / 8);
        writer.Write((short)(channels * bits / 8));
        writer.Write(bits);
        if (extraChunk)
        {
            writer.Write("LIST"u8);
            writer.Write(4);
            writer.Write("INFO"u8);
        }

        writer.Write("data"u8);
        writer.Write(dataBytes);
        writer.Write(new byte[dataBytes]);
        writer.Flush();

        var bytes = stream.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), bytes.Length - 8);
        return new MemoryStream(bytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TryRead_ShouldAccept16kHzMono16BitPcm_AndRestoreThePosition(bool extraChunk)
    {
        using var stream = Wav(extraChunk: extraChunk);

        WavHeader.TryRead(stream, out var header, out var error).Should().BeTrue(error);

        header.Should().Be(new WavHeader(16000, 1, 16, 3200));
        header!.Duration.Should().Be(TimeSpan.FromMilliseconds(100));
        stream.Position.Should().Be(0);
    }

    [Fact]
    public void TryRead_ShouldRejectAnotherSampleRate() =>
        Reject(Wav(sampleRate: 44100), "The WAV audio must be 16000 Hz, mono, 16-bit.");

    [Fact]
    public void TryRead_ShouldRejectStereo() =>
        Reject(Wav(channels: 2), "The WAV audio must be 16000 Hz, mono, 16-bit.");

    [Fact]
    public void TryRead_ShouldRejectFloatSamples() =>
        Reject(Wav(bits: 32, format: 3), "The WAV audio must be uncompressed PCM.");

    [Fact]
    public void TryRead_ShouldRejectAnEmptyClip() =>
        Reject(Wav(dataBytes: 0), "The WAV audio is empty.");

    [Fact]
    public void TryRead_ShouldRejectATruncatedHeader()
    {
        var bytes = Wav().ToArray()[..30];
        Reject(new MemoryStream(bytes), "The WAV header is incomplete.");
    }

    [Fact]
    public void TryRead_ShouldRejectWebm() =>
        Reject(new MemoryStream([0x1A, 0x45, 0xDF, 0xA3, 0x9F, 0x42, 0x86, 0x81, 0x01, 0x42, 0xF7, 0x81, 0x01]), "The audio isn't a WAV file.");

    [Fact]
    public void TryRead_ShouldRejectOgg() =>
        Reject(new MemoryStream("OggS\0\u0002\0\0\0\0\0\0\0\0"u8.ToArray()), "The audio isn't a WAV file.");

    [Fact]
    public void TryRead_ShouldRejectAnEmptyStream() =>
        Reject(new MemoryStream(), "The audio isn't a WAV file.");

    private static void Reject(Stream stream, string expected)
    {
        WavHeader.TryRead(stream, out var header, out var error).Should().BeFalse();
        header.Should().BeNull();
        error.Should().Be(expected);
    }
}
