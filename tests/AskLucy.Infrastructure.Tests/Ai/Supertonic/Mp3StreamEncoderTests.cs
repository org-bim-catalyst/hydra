using AskLucy.Infrastructure.Ai.Supertonic;
using FluentAssertions;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Ai.Supertonic;

/// <summary>specs/070 — the float PCM → MP3 stream Supertonic's audio leaves the server as.</summary>
public sealed class Mp3StreamEncoderTests
{
    private const int SampleRate = 44_100;

    private static float[] Tone(double seconds)
    {
        var samples = new float[(int)(seconds * SampleRate)];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)(0.5 * Math.Sin(2 * Math.PI * 440 * i / SampleRate));
        }

        return samples;
    }

    [Fact]
    public void EncodeThenFinish_ShouldProduceMpegAudioFrames_OfTheExpectedSize()
    {
        using var encoder = new Mp3StreamEncoder(SampleRate, 96);

        var audio = encoder.Encode(Tone(1.0)).Concat(encoder.Finish()).ToArray();

        // Frame sync: eleven set bits.
        audio.Length.Should().BeGreaterThan(2);
        audio[0].Should().Be(0xFF);
        (audio[1] & 0xE0).Should().Be(0xE0);

        // One second at 96 kbps is 12,000 bytes; allow for the encoder's leading/trailing frames.
        audio.Length.Should().BeInRange(10_000, 16_000);
    }

    [Fact]
    public void Encode_ShouldStreamAcrossCalls_WithoutRestartingTheStream()
    {
        using var encoder = new Mp3StreamEncoder(SampleRate, 96);

        var first = encoder.Encode(Tone(0.5));
        var second = encoder.Encode(Tone(0.5));
        var tail = encoder.Finish();

        first.Should().NotBeEmpty();
        second.Should().NotBeEmpty();
        (first.Length + second.Length + tail.Length).Should().BeInRange(10_000, 16_000);
    }

    [Fact]
    public void ToPcm16_ShouldClampAndWriteLittleEndian()
    {
        var pcm = Mp3StreamEncoder.ToPcm16([0f, 1f, -1f, 2f]);

        pcm.Should().Equal(
            0x00, 0x00,
            0xFF, 0x7F,
            0x01, 0x80,
            0xFF, 0x7F);
    }
}
