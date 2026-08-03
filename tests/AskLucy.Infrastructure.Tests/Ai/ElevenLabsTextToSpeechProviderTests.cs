using System.Net;
using System.Text;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Infrastructure.Ai;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Ai;

/// <summary>tasks.md T033 — proves <see cref="ElevenLabsTextToSpeechProvider"/> streams audio
/// bytes as they arrive and maps HTTP outcomes to the shared provider-exception types, against
/// recorded/replayed HTTP responses.</summary>
public sealed class ElevenLabsTextToSpeechProviderTests
{
    private static readonly VoiceSettingsDto Settings = new(
        VoiceId: "voice-1", ModelId: "eleven_v3", Stability: 0.5, SimilarityBoost: 0.75,
        Style: 0.0, Speed: 1.0, UseSpeakerBoost: true, OutputFormat: "mp3_44100_128");

    private static ElevenLabsTextToSpeechProvider CreateProvider(
        Func<HttpRequestMessage, HttpResponseMessage> responder, out StubHttpMessageHandler handler)
    {
        handler = new StubHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.elevenlabs.io/v1/") };
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("ElevenLabs").Returns(httpClient);

        var options = Options.Create(new ElevenLabsOptions { ApiKey = "raw-api-key" });
        return new ElevenLabsTextToSpeechProvider(factory, options);
    }

    [Fact]
    public async Task StreamSpeechAsync_ShouldYieldAudioBytes_AndSendXiApiKeyHeader()
    {
        var audioBytes = Encoding.UTF8.GetBytes("fake-mp3-bytes");
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(audioBytes),
        }, out var handler);

        var received = new List<byte>();
        await foreach (var chunk in provider.StreamSpeechAsync("Hello there.", Settings, CancellationToken.None))
        {
            received.AddRange(chunk);
        }

        received.Should().Equal(audioBytes);
        handler.LastRequest!.Headers.GetValues("xi-api-key").Should().ContainSingle().Which.Should().Be("raw-api-key");
        handler.LastRequest.RequestUri!.ToString().Should().Contain("text-to-speech/voice-1/stream");
    }

    [Fact]
    public async Task StreamSpeechAsync_ShouldThrowAiProviderAuthenticationException_When401()
    {
        var provider = CreateProvider(
            _ => new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{}") }, out _);

        var act = async () =>
        {
            await foreach (var _ in provider.StreamSpeechAsync("Hello.", Settings, CancellationToken.None))
            {
            }
        };

        await act.Should().ThrowAsync<AiProviderAuthenticationException>();
    }

    [Fact]
    public async Task StreamSpeechAsync_ShouldThrowAiProviderRateLimitedException_When429()
    {
        var provider = CreateProvider(
            _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("{}") }, out _);

        var act = async () =>
        {
            await foreach (var _ in provider.StreamSpeechAsync("Hello.", Settings, CancellationToken.None))
            {
            }
        };

        await act.Should().ThrowAsync<AiProviderRateLimitedException>();
    }
}
