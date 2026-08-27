using System.Net;
using System.Text;
using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;
using AskLucy.Infrastructure.Ai;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Ai;

/// <summary>specs/005-multi-provider-ai-engine T061 — proves <see cref="GoogleGeminiProvider"/> satisfies <see cref="IAIProvider"/> against mocked HTTP responses.</summary>
public sealed class GoogleGeminiProviderTests
{
    private readonly IAIProviderRepository _providers = Substitute.For<IAIProviderRepository>();
    private readonly IAiCredentialProtector _credentialProtector = Substitute.For<IAiCredentialProtector>();
    private readonly AIProvider _provider;

    public GoogleGeminiProviderTests()
    {
        _provider = AIProvider.Create("google-gemini", "Google Gemini", "test");
        _provider.SetCredential("ciphertext", "test");
        _providers.GetByKeyAsync("google-gemini", Arg.Any<CancellationToken>()).Returns(_provider);
        _credentialProtector.Unprotect("ciphertext").Returns("raw-api-key");
    }

    private GoogleGeminiProvider CreateProvider(Func<HttpRequestMessage, HttpResponseMessage> responder, out StubHttpMessageHandler handler)
    {
        handler = new StubHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/") };
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("GoogleGemini").Returns(httpClient);

        var options = Options.Create(new GoogleGeminiOptions { ApiKey = "", ChatModel = "gemini-1.5-pro" });
        return new GoogleGeminiProvider(factory, options, _providers, _credentialProtector, Substitute.For<ILogger<GoogleGeminiProvider>>());
    }

    [Fact]
    public async Task ChatAsync_ShouldSendTheApiKeyAsAQueryParameter_AndParseTheResponse()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"candidates":[{"content":{"parts":[{"text":"Hello!"}]}}],"usageMetadata":{"promptTokenCount":10,"candidatesTokenCount":5}}""",
                Encoding.UTF8, "application/json"),
        }, out var handler);

        var result = await provider.ChatAsync([new ChatMessage(ChatRole.User, "Hi")], "gemini-1.5-pro", parameters: null, CancellationToken.None);

        result.Content.Should().Be("Hello!");
        result.Usage.InputTokenCount.Should().Be(10);
        result.Usage.OutputTokenCount.Should().Be(5);
        handler.LastRequest!.RequestUri!.Query.Should().Contain("key=raw-api-key");
    }

    [Fact]
    public async Task ChatAsync_ShouldThrowAiProviderAuthenticationException_When403()
    {
        var provider = CreateProvider(
            _ => new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{}") }, out _);

        var act = () => provider.ChatAsync([new ChatMessage(ChatRole.User, "Hi")], "gemini-1.5-pro", null, CancellationToken.None);

        await act.Should().ThrowAsync<AiProviderAuthenticationException>();
    }

    [Fact]
    public async Task ChatAsync_ShouldThrowAiProviderAuthenticationException_WhenNoCredentialIsConfigured()
    {
        var uncredentialedProvider = AIProvider.Create("google-gemini", "Google Gemini", "test");
        _providers.GetByKeyAsync("google-gemini", Arg.Any<CancellationToken>()).Returns(uncredentialedProvider);
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK), out _);

        var act = () => provider.ChatAsync([new ChatMessage(ChatRole.User, "Hi")], "gemini-1.5-pro", null, CancellationToken.None);

        await act.Should().ThrowAsync<AiProviderAuthenticationException>();
    }

    [Fact]
    public async Task StreamChatAsync_ShouldYieldContentDeltasThenFinalUsage()
    {
        const string sse =
            """data: {"candidates":[{"content":{"parts":[{"text":"Hello"}]}}]}""" + "\n\n" +
            """data: {"candidates":[{"content":{"parts":[{"text":" world"}]}}]}""" + "\n\n" +
            """data: {"candidates":[{"content":{"parts":[]}}],"usageMetadata":{"promptTokenCount":7,"candidatesTokenCount":3}}""" + "\n\n";

        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse, Encoding.UTF8, "text/event-stream"),
        }, out _);

        var chunks = new List<StreamChunk>();
        await foreach (var chunk in provider.StreamChatAsync([new ChatMessage(ChatRole.User, "Hi")], "gemini-1.5-pro", null, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        chunks.Where(c => !string.IsNullOrEmpty(c.ContentDelta)).Select(c => c.ContentDelta).Should().Equal("Hello", " world");
        var finalChunk = chunks.Should().ContainSingle(c => c.Usage != null).Subject;
        finalChunk.Usage!.InputTokenCount.Should().Be(7);
        finalChunk.Usage!.OutputTokenCount.Should().Be(3);
    }

    // Live bug (2026-08-27): Gemini's real catalog spans embeddings/TTS/image/video/live-audio
    // models alongside chat models, and some of those report inputTokenLimit/outputTokenLimit
    // as a JSON null rather than omitting the field — GetInt32() on a null-kind element used to
    // throw, taking down the ENTIRE "sync catalog" admin action with a generic 500 for one
    // unrelated model entry.
    [Fact]
    public async Task ListAvailableModelsAsync_ShouldTreatANullTokenLimit_AsZero_NotThrow()
    {
        const string json = """
            {
                "models": [
                    { "name": "models/gemini-1.5-pro", "displayName": "Gemini 1.5 Pro", "inputTokenLimit": 2000000, "outputTokenLimit": 8192 },
                    { "name": "models/text-embedding-004", "displayName": "Text Embedding 004", "inputTokenLimit": null, "outputTokenLimit": null }
                ]
            }
            """;
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        }, out _);

        var models = await provider.ListAvailableModelsAsync(CancellationToken.None);

        models.Should().HaveCount(2);
        models.Should().ContainSingle(m => m.ModelKey == "gemini-1.5-pro" && m.ContextWindowTokens == 2_000_000 && m.MaxOutputTokens == 8192);
        models.Should().ContainSingle(m => m.ModelKey == "text-embedding-004" && m.ContextWindowTokens == 0 && m.MaxOutputTokens == 0);
    }
}
