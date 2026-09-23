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

/// <summary>tasks.md T033 — proves <see cref="ElevenLabsTextToSpeechEngine"/> streams audio
/// bytes as they arrive and maps HTTP outcomes to the shared provider-exception types, against
/// recorded/replayed HTTP responses. specs/070 added the per-provider credential and voice list.</summary>
public sealed class ElevenLabsTextToSpeechEngineTests
{
    private static readonly VoiceSettingsDto Settings = new(
        VoiceId: "voice-1", ModelId: "eleven_v3", Stability: 0.5, SimilarityBoost: 0.75,
        Style: 0.0, Speed: 1.0, UseSpeakerBoost: true, OutputFormat: "mp3_44100_128");

    private static ElevenLabsTextToSpeechEngine CreateEngine(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        out StubHttpMessageHandler handler,
        string configuredApiKey = "raw-api-key",
        IReadOnlyDictionary<string, string>? voiceIdByLanguage = null)
    {
        handler = new StubHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.elevenlabs.io/v1/") };
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("ElevenLabs").Returns(httpClient);

        var options = Options.Create(new ElevenLabsOptions
        {
            ApiKey = configuredApiKey,
            VoiceId = "platform-voice",
            VoiceIdByLanguage = voiceIdByLanguage ?? new Dictionary<string, string>(),
        });
        return new ElevenLabsTextToSpeechEngine(factory, options);
    }

    private static async Task DrainAsync(IAsyncEnumerable<byte[]> stream)
    {
        await foreach (var _ in stream)
        {
        }
    }

    [Fact]
    public async Task StreamSpeechAsync_ShouldYieldAudioBytes_AndSendXiApiKeyHeader()
    {
        var audioBytes = Encoding.UTF8.GetBytes("fake-mp3-bytes");
        var engine = CreateEngine(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(audioBytes),
        }, out var handler);

        var received = new List<byte>();
        await foreach (var chunk in engine.StreamSpeechAsync("Hello there.", Settings, null, CancellationToken.None))
        {
            received.AddRange(chunk);
        }

        received.Should().Equal(audioBytes);
        handler.LastRequest!.Headers.GetValues("xi-api-key").Should().ContainSingle().Which.Should().Be("raw-api-key");
        handler.LastRequest.RequestUri!.ToString().Should().Contain("text-to-speech/voice-1/stream");
    }

    [Fact]
    public async Task StreamSpeechAsync_ShouldPreferTheProviderCredential_OverTheConfiguredKey()
    {
        var engine = CreateEngine(
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) }, out var handler);

        await DrainAsync(engine.StreamSpeechAsync("Hello.", Settings, "admin-set-key", CancellationToken.None));

        handler.LastRequest!.Headers.GetValues("xi-api-key").Should().ContainSingle().Which.Should().Be("admin-set-key");
    }

    [Fact]
    public async Task StreamSpeechAsync_ShouldThrowNotConfigured_WhenNeitherKeyIsSet()
    {
        var engine = CreateEngine(_ => new HttpResponseMessage(HttpStatusCode.OK), out var handler, configuredApiKey: "");

        var act = () => DrainAsync(engine.StreamSpeechAsync("Hello.", Settings, null, CancellationToken.None));

        await act.Should().ThrowAsync<AiProviderNotConfiguredException>();
        handler.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task StreamSpeechAsync_ShouldThrowAiProviderAuthenticationException_When401()
    {
        var engine = CreateEngine(
            _ => new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{}") }, out _);

        var act = () => DrainAsync(engine.StreamSpeechAsync("Hello.", Settings, null, CancellationToken.None));

        await act.Should().ThrowAsync<AiProviderAuthenticationException>();
    }

    [Fact]
    public async Task StreamSpeechAsync_ShouldThrowAiProviderRateLimitedException_When429()
    {
        var engine = CreateEngine(
            _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("{}") }, out _);

        var act = () => DrainAsync(engine.StreamSpeechAsync("Hello.", Settings, null, CancellationToken.None));

        await act.Should().ThrowAsync<AiProviderRateLimitedException>();
    }

    [Fact]
    public async Task ListVoicesAsync_ShouldMapVoicesWithGenderAndDescription()
    {
        const string body = """
            {"voices":[
              {"voice_id":"v1","name":"Rachel","category":"premade","labels":{"gender":"female","accent":"american","age":"young"}},
              {"voice_id":"v2","name":"","category":null,"labels":null},
              {"voice_id":"","name":"Nameless"}
            ]}
            """;
        var engine = CreateEngine(
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") },
            out var handler);

        var voices = await engine.ListVoicesAsync("admin-set-key", CancellationToken.None);

        voices.Should().BeEquivalentTo(new[]
        {
            new VoiceOptionDto("v1", "Rachel", "female", "american, young, premade"),
            new VoiceOptionDto("v2", "v2", null, null),
        });
        handler.LastRequest!.RequestUri!.ToString().Should().EndWith("/voices");
        handler.LastRequest.Headers.GetValues("xi-api-key").Should().ContainSingle().Which.Should().Be("admin-set-key");
    }

    [Fact]
    public async Task ListVoicesAsync_ShouldThrowResponseInvalid_WhenTheBodyIsNotJson()
    {
        var engine = CreateEngine(
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>", Encoding.UTF8, "application/json") },
            out _);

        var act = () => engine.ListVoicesAsync(null, CancellationToken.None);

        await act.Should().ThrowAsync<AiProviderResponseInvalidException>();
    }

    [Theory]
    [InlineData("chosen-voice", "ar", "chosen-voice")]
    [InlineData(null, "ar", "arabic-voice")]
    [InlineData(null, "fr", "platform-voice")]
    public void ResolveDefaultSettings_ShouldPreferExplicitVoice_ThenLanguageMapping_ThenPlatformDefault(
        string? voiceId, string language, string expectedVoiceId)
    {
        var engine = CreateEngine(
            _ => new HttpResponseMessage(HttpStatusCode.OK), out _,
            voiceIdByLanguage: new Dictionary<string, string> { ["ar"] = "arabic-voice" });

        var settings = engine.ResolveDefaultSettings(language, voiceId);

        settings.VoiceId.Should().Be(expectedVoiceId);
        settings.Language.Should().Be(language);
        settings.ProviderKey.Should().Be(ElevenLabsTextToSpeechEngine.Key);
    }
}
