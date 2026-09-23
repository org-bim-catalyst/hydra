using GroovyCodecs.Mp3;
using GroovyCodecs.Types;

namespace AskLucy.Infrastructure.Ai.Supertonic;

/// <summary>
/// One continuous mono CBR MP3 stream, fed float PCM a piece at a time (specs/070). MP3 because it
/// is what ElevenLabs already returns — the client's MediaSource pipeline plays <c>audio/mpeg</c>
/// and needs no second code path — and it is ~7× smaller than WAV at 96 kbps.
///
/// <para>Uses GroovyMp3 (LGPL-3.0, consumed unmodified as a NuGet package). Its encoder state is
/// per instance, so concurrent replies each get their own.</para>
/// </summary>
internal sealed class Mp3StreamEncoder : IDisposable
{
    private readonly Mp3Encoder _encoder;
    private readonly byte[] _output;
    private bool _closed;

    public Mp3StreamEncoder(int sampleRate, int bitRate)
    {
        var format = new AudioFormat
        {
            SampleRate = sampleRate,
            Channels = 1,
            BitsPerSample = 16,
            BigEndian = false,
            IsFloatingPoint = false,
        };
        _encoder = new Mp3Encoder(format, bitRate, Mp3Encoder.CHANNEL_MODE_MONO, Mp3Encoder.QUALITY_MIDDLE, false);
        _output = new byte[_encoder.OutputBufferSize];
    }

    /// <summary>Encodes <paramref name="samples"/> and returns whatever whole frames are ready —
    /// possibly none, since the encoder holds back a partial frame until more audio arrives.</summary>
    public byte[] Encode(ReadOnlySpan<float> samples)
    {
        var pcm = ToPcm16(samples);
        using var encoded = new MemoryStream();
        for (var offset = 0; offset < pcm.Length; offset += _encoder.PCMBufferSize)
        {
            var length = Math.Min(_encoder.PCMBufferSize, pcm.Length - offset);
            var written = _encoder.EncodeBuffer(pcm, offset, length, _output);
            encoded.Write(_output, 0, written);
        }

        return encoded.ToArray();
    }

    /// <summary>Flushes the held-back partial frame. Call once, after the last <see cref="Encode"/>.</summary>
    public byte[] Finish()
    {
        var written = _encoder.EncodeFinish(_output);
        return _output[..written];
    }

    public void Dispose()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _encoder.Close();
    }

    internal static byte[] ToPcm16(ReadOnlySpan<float> samples)
    {
        var pcm = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            var sample = (short)(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);
            pcm[2 * i] = (byte)sample;
            pcm[(2 * i) + 1] = (byte)(sample >> 8);
        }

        return pcm;
    }
}
