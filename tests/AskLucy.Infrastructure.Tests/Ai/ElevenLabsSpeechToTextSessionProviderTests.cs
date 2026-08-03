using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AskLucy.Application.Abstractions;
using AskLucy.Infrastructure.Ai;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Ai;

/// <summary>tasks.md T033 — proves <see cref="ElevenLabsSpeechToTextSessionProvider"/> maps
/// HTTP outcomes to the shared provider-exception types (research.md Decisions 2/9) against
/// recorded/replayed HTTP responses, never a live ElevenLabs call.</summary>
public sealed class ElevenLabsSpeechToTextSessionProviderTests
{
    private static ElevenLabsSpeechToTextSessionProvider CreateProvider(
        Func<HttpRequestMessage, HttpResponseMessage> responder, out StubHttpMessageHandler handler)
    {
        handler = new StubHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.elevenlabs.io/v1/") };
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("ElevenLabs").Returns(httpClient);

        var options = Options.Create(new ElevenLabsOptions { ApiKey = "raw-api-key", ModelId = "eleven_v3" });
        return new ElevenLabsSpeechToTextSessionProvider(factory, options);
    }

    [Fact]
    public async Task CreateSessionAsync_ShouldSendXiApiKeyHeader_AndParseTheToken()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"token":"short-lived-token"}""", Encoding.UTF8, "application/json"),
        }, out var handler);

        var session = await provider.CreateSessionAsync("en", CancellationToken.None);

        session.Token.Should().Be("short-lived-token");
        session.ExpiresAtUtc.Should().BeAfter(DateTime.UtcNow);
        handler.LastRequest!.Headers.GetValues("xi-api-key").Should().ContainSingle().Which.Should().Be("raw-api-key");
    }

    /// <summary>Production incident, SPEC-013: the original path (`speech-to-text/realtime/token`)
    /// was never actually called until SPEC-013 wired a live mic control, and 404'd against
    /// every real request. Locks in the verified path
    /// (https://elevenlabs.io/docs/api-reference/tokens/create) so it can't silently regress.</summary>
    [Fact]
    public async Task CreateSessionAsync_ShouldPostToTheVerifiedSingleUseTokenEndpoint()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"token":"short-lived-token"}""", Encoding.UTF8, "application/json"),
        }, out var handler);

        await provider.CreateSessionAsync("en", CancellationToken.None);

        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri.Should().Be(
            new Uri("https://api.elevenlabs.io/v1/single-use-token/realtime_scribe"));
    }

    [Fact]
    public async Task CreateSessionAsync_ShouldThrowAiProviderAuthenticationException_When401()
    {
        var provider = CreateProvider(
            _ => new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{}") }, out _);

        var act = () => provider.CreateSessionAsync("en", CancellationToken.None);

        await act.Should().ThrowAsync<AiProviderAuthenticationException>();
    }

    [Fact]
    public async Task CreateSessionAsync_ShouldThrowAiProviderRateLimitedException_When429()
    {
        var provider = CreateProvider(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("{}") };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(15));
            return response;
        }, out _);

        var act = () => provider.CreateSessionAsync("en", CancellationToken.None);

        (await act.Should().ThrowAsync<AiProviderRateLimitedException>()).Which.RetryAfter.Should().Be(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task CreateSessionAsync_ShouldThrowAiProviderUnavailableException_OnServerError()
    {
        var provider = CreateProvider(
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("upstream down") }, out _);

        var act = () => provider.CreateSessionAsync("en", CancellationToken.None);

        await act.Should().ThrowAsync<AiProviderUnavailableException>();
    }
}
