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

/// <summary>specs/005-multi-provider-ai-engine T061 — proves <see cref="OpenRouterProvider"/> satisfies <see cref="IAIProvider"/> against mocked HTTP responses.</summary>
public sealed class OpenRouterProviderTests
{
    private readonly IAIProviderRepository _providers = Substitute.For<IAIProviderRepository>();
    private readonly IAiCredentialProtector _credentialProtector = Substitute.For<IAiCredentialProtector>();
    private readonly AIProvider _provider;

    public OpenRouterProviderTests()
    {
        _provider = AIProvider.Create("openrouter", "OpenRouter", "test");
        _provider.SetCredential("ciphertext", "test");
        _providers.GetByKeyAsync("openrouter", Arg.Any<CancellationToken>()).Returns(_provider);
        _credentialProtector.Unprotect("ciphertext").Returns("raw-api-key");
    }

    private OpenRouterProvider CreateProvider(Func<HttpRequestMessage, HttpResponseMessage> responder, out StubHttpMessageHandler handler)
    {
        handler = new StubHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://openrouter.ai/api/v1/") };
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("OpenRouter").Returns(httpClient);

        var options = Options.Create(new OpenRouterOptions { ApiKey = "", ChatModel = "openai/gpt-4o" });
        return new OpenRouterProvider(factory, options, _providers, _credentialProtector, Substitute.For<ILogger<OpenRouterProvider>>());
    }

    [Fact]
    public async Task ChatAsync_ShouldSendBearerTokenFromTheEncryptedCredential_AndParseTheResponse()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"choices":[{"message":{"content":"Hello!"}}],"usage":{"prompt_tokens":10,"completion_tokens":5}}""",
                Encoding.UTF8, "application/json"),
        }, out var handler);

        var result = await provider.ChatAsync([new ChatMessage(ChatRole.User, "Hi")], "openai/gpt-4o", parameters: null, CancellationToken.None);

        result.Content.Should().Be("Hello!");
        result.Usage.InputTokenCount.Should().Be(10);
        result.Usage.OutputTokenCount.Should().Be(5);
        handler.LastRequest!.Headers.Authorization!.Parameter.Should().Be("raw-api-key");
    }

    [Fact]
    public async Task ChatAsync_ShouldThrowAiProviderAuthenticationException_When401()
    {
        var provider = CreateProvider(
            _ => new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{}") }, out _);

        var act = () => provider.ChatAsync([new ChatMessage(ChatRole.User, "Hi")], "openai/gpt-4o", null, CancellationToken.None);

        await act.Should().ThrowAsync<AiProviderAuthenticationException>();
    }

    [Fact]
    public async Task ChatAsync_ShouldThrowAiProviderAuthenticationException_WhenNoCredentialIsConfigured()
    {
        var uncredentialedProvider = AIProvider.Create("openrouter", "OpenRouter", "test");
        _providers.GetByKeyAsync("openrouter", Arg.Any<CancellationToken>()).Returns(uncredentialedProvider);
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK), out _);

        var act = () => provider.ChatAsync([new ChatMessage(ChatRole.User, "Hi")], "openai/gpt-4o", null, CancellationToken.None);

        await act.Should().ThrowAsync<AiProviderAuthenticationException>();
    }

    [Fact]
    public async Task StreamChatAsync_ShouldYieldContentDeltasThenFinalUsage()
    {
        const string sse =
            """data: {"choices":[{"delta":{"content":"Hello"}}]}""" + "\n\n" +
            """data: {"choices":[{"delta":{"content":" world"}}]}""" + "\n\n" +
            """data: {"choices":[],"usage":{"prompt_tokens":7,"completion_tokens":3}}""" + "\n\n" +
            "data: [DONE]\n\n";

        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse, Encoding.UTF8, "text/event-stream"),
        }, out _);

        var chunks = new List<StreamChunk>();
        await foreach (var chunk in provider.StreamChatAsync([new ChatMessage(ChatRole.User, "Hi")], "openai/gpt-4o", null, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        chunks.Where(c => !string.IsNullOrEmpty(c.ContentDelta)).Select(c => c.ContentDelta).Should().Equal("Hello", " world");
        var finalChunk = chunks.Should().ContainSingle(c => c.Usage != null).Subject;
        finalChunk.Usage!.InputTokenCount.Should().Be(7);
        finalChunk.Usage!.OutputTokenCount.Should().Be(3);
    }
}
