using System.Net;
using System.Text;
using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;
using AskLucy.Infrastructure.Ai;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
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
    private readonly IAIProviderRepository _providers = Substitute.For<IAIProviderRepository>();
    private readonly IAiCredentialProtector _credentialProtector = Substitute.For<IAiCredentialProtector>();

    /// <summary>
    /// Stores the administrator's credential, which this provider now prefers over
    /// <c>OpenAI:ApiKey</c> — the same source <c>OpenAiEmbeddingProvider</c> reads, so the two
    /// can no longer end up authenticating as different keys.
    /// </summary>
    public OpenAIProviderTests()
    {
        var provider = AIProvider.Create("openai", "OpenAI", "test");
        provider.SetCredential("ciphertext", null, "test");
        _providers.GetByKeyAsync("openai", Arg.Any<CancellationToken>()).Returns(provider);
        _credentialProtector.Unprotect("ciphertext").Returns("stored-api-key");
    }

    private OpenAIProvider CreateProvider(Func<HttpRequestMessage, HttpResponseMessage> responder, out StubHttpMessageHandler handler)
    {
        var stubHandler = new StubHttpMessageHandler(responder);
        handler = stubHandler;
        var factory = Substitute.For<IHttpClientFactory>();
        // A fresh HttpClient per call (disposeHandler: false) — OpenAIProvider disposes the
        // client it gets from CreateClient() after every call, including retries, so a single
        // shared instance would already be disposed by the time WithRetryAsync's retry fires.
        factory.CreateClient("OpenAI").Returns(_ => new HttpClient(stubHandler, disposeHandler: false));

        var options = Options.Create(new OpenAIOptions { ApiKey = "test-key", BaseUrl = "https://api.openai.com/v1/" });
        return new OpenAIProvider(factory, options, _providers, _credentialProtector, Substitute.For<ILogger<OpenAIProvider>>());
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

        // specs/043 FR-013/SC-008: the vendor's own text must NOT reach the exception message,
        // because that message becomes the Problem Details `detail` a client reads. This
        // assertion previously required the leak; it now forbids it. The body is still
        // recorded server-side, truncated, by the classifier's structured log.
        (await act.Should().ThrowAsync<AiProviderRequestInvalidException>())
            .Which.Message.Should().NotContain("Invalid file format");
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

    // specs/040-composer-interaction-bug-fixes US6 follow-up — browsers send codec-parameterised
    // MIME types such as "audio/webm;codecs=opus". MediaTypeHeaderValue rejects the parameterised
    // form; the provider must strip the codec suffix before constructing the header value.
    [Fact]
    public async Task TranscribeAudioAsync_ShouldReturnText_WhenContentTypeHasCodecParameter()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"text": "hello from whisper"}""", Encoding.UTF8, "application/json"),
        }, out _);

        var result = await provider.TranscribeAudioAsync(
            new MemoryStream([1, 2, 3]), "recording.webm", "audio/webm;codecs=opus", CancellationToken.None);

        result.Should().Be("hello from whisper");
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
        var provider = new OpenAIProvider(factory, options, _providers, _credentialProtector, Substitute.For<ILogger<OpenAIProvider>>());

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
        // No stored credential, so configuration is genuinely the only source — otherwise the
        // fixture's stored key would satisfy the request and this would stop testing anything.
        _providers.GetByKeyAsync("openai", Arg.Any<CancellationToken>()).Returns((AIProvider?)null);
        var provider = new OpenAIProvider(factory, options, _providers, _credentialProtector, Substitute.For<ILogger<OpenAIProvider>>());

        var act = () => TranscribeAsync(provider);

        await act.Should().ThrowAsync<AiProviderAuthenticationException>()
            .WithMessage("*not configured with an API key*");
    }

    [Fact]
    public async Task ListAvailableModelsAsync_ShouldReportAbsentTokenLimits_AsNull()
    {
        // specs/043 FR-029/SC-006. OpenAI publishes no token metadata for any model, so before
        // this change every one of its ~97 rows failed to apply with "Context window must be
        // greater than zero" - permanently, since no edit path for the figures exists.
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"data":[{"id":"gpt-4-turbo"}]}""", Encoding.UTF8, "application/json"),
        }, out _);

        var models = await provider.ListAvailableModelsAsync(CancellationToken.None);

        models.Should().ContainSingle(m => m.ModelKey == "gpt-4-turbo" && m.ContextWindowTokens == null && m.MaxOutputTokens == null);
    }

    private static HttpResponseMessage ImageResponse(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task GenerateImageAsync_ShouldReturnBase64_ForGptImageModelsWhichNeverReturnAUrl()
    {
        // The exact production failure: gpt-image-* answered 200 with b64_json only, and the
        // provider read data[0].url, throwing KeyNotFoundException after a successful, billed call.
        string? requestBody = null;
        var provider = CreateProvider(request =>
        {
            requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return ImageResponse("""{"data":[{"b64_json":"iVBORw0KGgo="}]}""");
        }, out _);

        var payload = await provider.GenerateImageAsync("a map", "gpt-image-2", CancellationToken.None);

        payload.Should().BeOfType<GeneratedImagePayload.Base64>().Which.Data.Should().Be("iVBORw0KGgo=");
        requestBody.Should().Contain("\"model\":\"gpt-image-2\"")
            .And.NotContain("response_format");
    }

    [Fact]
    public async Task GenerateImageAsync_ShouldReturnARemoteUrl_ForModelsThatReturnOne()
    {
        var provider = CreateProvider(_ => ImageResponse("""{"data":[{"url":"https://files.example/img.png"}]}"""), out _);

        var payload = await provider.GenerateImageAsync("a map", "dall-e-3", CancellationToken.None);

        payload.Should().BeOfType<GeneratedImagePayload.RemoteUrl>().Which.Url.Should().Be(new Uri("https://files.example/img.png"));
    }

    [Fact]
    public async Task GenerateImageAsync_ShouldThrowUnavailable_WhenTheResponseCarriesNoImage()
    {
        var provider = CreateProvider(_ => ImageResponse("""{"data":[{}]}"""), out _);

        var act = () => provider.GenerateImageAsync("a map", "gpt-image-2", CancellationToken.None);

        await act.Should().ThrowAsync<AiProviderUnavailableException>();
    }

    [Fact]
    public async Task ListAvailableModelsAsync_ShouldFlagGptImageAndDallEModels_AsProducingImages()
    {
        var provider = CreateProvider(_ => ImageResponse(
            """{"data":[{"id":"gpt-5"},{"id":"gpt-image-2"},{"id":"dall-e-3"},{"id":"whisper-1"}]}"""), out _);

        var models = await provider.ListAvailableModelsAsync(CancellationToken.None);

        models.Select(m => m.ModelKey).Should().BeEquivalentTo(["gpt-5", "gpt-image-2", "dall-e-3"]);
        models.Single(m => m.ModelKey == "gpt-image-2").Capabilities.ImageOutput.Should().BeTrue();
        models.Single(m => m.ModelKey == "dall-e-3").Capabilities.ImageOutput.Should().BeTrue();
        models.Single(m => m.ModelKey == "gpt-5").Capabilities.ImageOutput.Should().BeFalse();
    }

    [Fact]
    public async Task ShouldAuthenticateWithTheStoredCredential_NotTheConfigurationKey()
    {
        // The whole point of unifying the two OpenAI call paths: whatever the administrator saved
        // is what every OpenAI request uses. Previously chat/images authenticated with
        // OpenAI:ApiKey while embeddings used this stored credential, so a wrong key in the admin
        // UI broke memory/RAG with a 401 while chat kept working — which reads as a provider
        // outage rather than a bad key.
        string? authorization = null;
        var provider = CreateProvider(request =>
        {
            authorization = request.Headers.Authorization?.Parameter;
            return ImageResponse("""{"data":[{"b64_json":"iVBORw0KGgo="}]}""");
        }, out _);

        await provider.GenerateImageAsync("a map", "gpt-image-2", CancellationToken.None);

        authorization.Should().Be("stored-api-key").And.NotBe("test-key");
    }

    [Fact]
    public async Task ShouldFallBackToConfiguration_WhenNoCredentialHasBeenStoredYet()
    {
        // A deployment that has not yet moved this provider's key into the database keeps working
        // — the fallback is what makes the move to database-held credentials incremental.
        _providers.GetByKeyAsync("openai", Arg.Any<CancellationToken>()).Returns((AIProvider?)null);

        string? authorization = null;
        var provider = CreateProvider(request =>
        {
            authorization = request.Headers.Authorization?.Parameter;
            return ImageResponse("""{"data":[{"b64_json":"iVBORw0KGgo="}]}""");
        }, out _);

        await provider.GenerateImageAsync("a map", "gpt-image-2", CancellationToken.None);

        authorization.Should().Be("test-key");
    }

    [Fact]
    public async Task ShouldReportAnUndecryptableCredential_RatherThanSilentlyUsingTheConfigurationKey()
    {
        _credentialProtector.Unprotect("ciphertext").Throws(new System.Security.Cryptography.CryptographicException("key ring changed"));
        var provider = CreateProvider(_ => ImageResponse("""{"data":[{"b64_json":"iVBORw0KGgo="}]}"""), out _);

        var act = () => provider.GenerateImageAsync("a map", "gpt-image-2", CancellationToken.None);

        await act.Should().ThrowAsync<AiProviderException>();
    }
}
