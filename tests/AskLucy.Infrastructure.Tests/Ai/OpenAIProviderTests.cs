using System.Net;
using System.Text;
using AskLucy.Application.Abstractions;
using AskLucy.Infrastructure.Ai;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Ai;

/// <summary>
/// specs/032-transcription-and-mode-switch-fixes T003 — proves <see cref="OpenAIProvider"/>
/// classifies every non-2xx response from OpenAI, closing the gap that previously let an
/// unclassified 4xx (e.g. a rejected transcription upload) fall through as a bare
/// <see cref="HttpRequestException"/> and surface to the client as a generic 500.
/// </summary>
public sealed class OpenAIProviderTests
{
    private static OpenAIProvider CreateProvider(Func<HttpRequestMessage, HttpResponseMessage> responder, out StubHttpMessageHandler handler)
    {
        var stubHandler = new StubHttpMessageHandler(responder);
        handler = stubHandler;
        var factory = Substitute.For<IHttpClientFactory>();
        // A fresh HttpClient per call (disposeHandler: false) — OpenAIProvider disposes the
        // client it gets from CreateClient() after every call, including retries, so a single
        // shared instance would already be disposed by the time WithRetryAsync's retry fires.
        factory.CreateClient("OpenAI").Returns(_ => new HttpClient(stubHandler, disposeHandler: false));

        var options = Options.Create(new OpenAIOptions { ApiKey = "test-key", BaseUrl = "https://api.openai.com/v1/" });
        return new OpenAIProvider(factory, options, Substitute.For<ILogger<OpenAIProvider>>());
    }

    private static Task<string> TranscribeAsync(OpenAIProvider provider) =>
        provider.TranscribeAudioAsync(new MemoryStream([1, 2, 3]), "recording.webm", "audio/webm", CancellationToken.None);

    [Fact]
    public async Task TranscribeAudioAsync_ShouldThrowAiProviderRequestInvalidException_When400()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"error":{"message":"Invalid file format"}}""", Encoding.UTF8, "application/json"),
        }, out _);

        var act = () => TranscribeAsync(provider);

        (await act.Should().ThrowAsync<AiProviderRequestInvalidException>())
            .WithMessage("*Invalid file format*");
    }

    [Fact]
    public async Task TranscribeAudioAsync_ShouldThrowAiProviderRequestInvalidException_When422()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        }, out _);

        var act = () => TranscribeAsync(provider);

        await act.Should().ThrowAsync<AiProviderRequestInvalidException>();
    }

    [Fact]
    public async Task TranscribeAudioAsync_ShouldStillThrowAiProviderAuthenticationException_When401()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{}"),
        }, out _);

        var act = () => TranscribeAsync(provider);

        await act.Should().ThrowAsync<AiProviderAuthenticationException>();
    }

    [Fact]
    public async Task TranscribeAudioAsync_ShouldStillThrowAiProviderAuthenticationException_When403()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("{}"),
        }, out _);

        var act = () => TranscribeAsync(provider);

        await act.Should().ThrowAsync<AiProviderAuthenticationException>();
    }

    [Fact]
    public async Task TranscribeAudioAsync_ShouldStillThrowAiProviderRateLimitedException_When429()
    {
        var provider = CreateProvider(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("{}") };
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(5));
            return response;
        }, out _);

        var act = () => TranscribeAsync(provider);

        var exception = await act.Should().ThrowAsync<AiProviderRateLimitedException>();
        exception.Which.RetryAfter.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task TranscribeAudioAsync_ShouldStillThrowAiProviderUnavailableException_When500AfterRetry()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("{}"),
        }, out _);

        var act = () => TranscribeAsync(provider);

        await act.Should().ThrowAsync<AiProviderUnavailableException>();
    }

    // specs/033-hold-to-talk-and-echo-fix T003: a 2xx response OpenAI shouldn't ever send but
    // that the client must still not crash on unclassified — previously an uncaught
    // JsonException/InvalidOperationException here surfaced as a generic 500 to the user.
    [Fact]
    public async Task TranscribeAudioAsync_ShouldThrowAiProviderUnavailableException_WhenBodyIsEmpty()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(string.Empty),
        }, out _);

        var act = () => TranscribeAsync(provider);

        await act.Should().ThrowAsync<AiProviderUnavailableException>();
    }

    [Fact]
    public async Task TranscribeAudioAsync_ShouldThrowAiProviderUnavailableException_WhenBodyIsNotJson()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not json at all", Encoding.UTF8, "application/json"),
        }, out _);

        var act = () => TranscribeAsync(provider);

        await act.Should().ThrowAsync<AiProviderUnavailableException>();
    }

    [Fact]
    public async Task TranscribeAudioAsync_ShouldThrowAiProviderUnavailableException_WhenTextPropertyIsMissing()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"duration": 3.2}""", Encoding.UTF8, "application/json"),
        }, out _);

        var act = () => TranscribeAsync(provider);

        await act.Should().ThrowAsync<AiProviderUnavailableException>();
    }

    [Fact]
    public async Task TranscribeAudioAsync_ShouldReturnText_WhenBodyIsWellFormed()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"text": "hello world"}""", Encoding.UTF8, "application/json"),
        }, out _);

        var result = await TranscribeAsync(provider);

        result.Should().Be("hello world");
    }

    // specs/040-composer-interaction-bug-fixes US6 follow-up — any non-transient, unclassified
    // exception that escapes the operation (e.g. a configuration fault or unexpected IO error)
    // must be wrapped as AiProviderUnavailableException, never allowed to reach the generic 500.
    [Fact]
    public async Task TranscribeAudioAsync_ShouldThrowAiProviderUnavailableException_WhenNonTransientUnclassifiedExceptionOccurs()
    {
        var stubHandler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("unexpected"));
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("OpenAI").Returns(_ => new HttpClient(stubHandler, disposeHandler: false));
        var options = Options.Create(new OpenAIOptions { ApiKey = "test-key", BaseUrl = "https://api.openai.com/v1/" });
        var provider = new OpenAIProvider(factory, options, Substitute.For<ILogger<OpenAIProvider>>());

        var act = () => TranscribeAsync(provider);

        await act.Should().ThrowAsync<AiProviderUnavailableException>();
    }

    // specs/040-composer-interaction-bug-fixes US6 T018 — a missing API key must surface as
    // AiProviderAuthenticationException (→ 502 via ProblemDetailsMiddleware), not as an
    // ArgumentNullException or NullReferenceException that would reach the generic 500 handler.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task TranscribeAudioAsync_ShouldThrowAiProviderAuthenticationException_WhenApiKeyIsNullOrWhitespace(string? apiKey)
    {
        var stubHandler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("OpenAI").Returns(_ => new HttpClient(stubHandler, disposeHandler: false));
        var options = Options.Create(new OpenAIOptions { ApiKey = apiKey!, BaseUrl = "https://api.openai.com/v1/" });
        var provider = new OpenAIProvider(factory, options, Substitute.For<ILogger<OpenAIProvider>>());

        var act = () => TranscribeAsync(provider);

        await act.Should().ThrowAsync<AiProviderAuthenticationException>()
            .WithMessage("*not configured with an API key*");
    }
}
