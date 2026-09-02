using System.Net;
using System.Net.Http.Headers;
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

/// <summary>specs/005-multi-provider-ai-engine T061 — proves <see cref="AnthropicProvider"/> satisfies <see cref="IAIProvider"/> against mocked HTTP responses, per research.md Decision 9's error-mapping contract.</summary>
public sealed class AnthropicProviderTests
{
    private readonly IAIProviderRepository _providers = Substitute.For<IAIProviderRepository>();
    private readonly IAiCredentialProtector _credentialProtector = Substitute.For<IAiCredentialProtector>();
    private readonly AIProvider _provider;

    public AnthropicProviderTests()
    {
        _provider = AIProvider.Create("anthropic", "Anthropic", "test");
        _provider.SetCredential("ciphertext", "test");
        _providers.GetByKeyAsync("anthropic", Arg.Any<CancellationToken>()).Returns(_provider);
        _credentialProtector.Unprotect("ciphertext").Returns("raw-api-key");
    }

    private AnthropicProvider CreateProvider(Func<HttpRequestMessage, HttpResponseMessage> responder, out StubHttpMessageHandler handler)
    {
        handler = new StubHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com/v1/") };
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("Anthropic").Returns(httpClient);

        var options = Options.Create(new AnthropicOptions { ApiKey = "", ChatModel = "claude-3-5-sonnet-20241022" });
        return new AnthropicProvider(factory, options, _providers, _credentialProtector, Substitute.For<ILogger<AnthropicProvider>>());
    }

    [Fact]
    public async Task ChatAsync_ShouldSendXApiKeyHeaderFromTheEncryptedCredential_AndParseTheResponse()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"content":[{"type":"text","text":"Hello!"}],"usage":{"input_tokens":10,"output_tokens":5}}""",
                Encoding.UTF8, "application/json"),
        }, out var handler);

        var result = await provider.ChatAsync(
            [new ChatMessage(ChatRole.User, "Hi")], "claude-3-5-sonnet-20241022", parameters: null, CancellationToken.None);

        result.Content.Should().Be("Hello!");
        result.Usage.InputTokenCount.Should().Be(10);
        result.Usage.OutputTokenCount.Should().Be(5);
        handler.LastRequest!.Headers.GetValues("x-api-key").Should().ContainSingle().Which.Should().Be("raw-api-key");
    }

    [Fact]
    public async Task ChatAsync_ShouldThrowAiProviderAuthenticationException_When401()
    {
        var provider = CreateProvider(
            _ => new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{}") }, out _);

        var act = () => provider.ChatAsync([new ChatMessage(ChatRole.User, "Hi")], "claude-3-5-sonnet-20241022", null, CancellationToken.None);

        await act.Should().ThrowAsync<AiProviderAuthenticationException>();
    }

    [Fact]
    public async Task ChatAsync_ShouldThrowAiProviderRateLimitedException_When429()
    {
        var provider = CreateProvider(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("{}") };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return response;
        }, out _);

        var act = () => provider.ChatAsync([new ChatMessage(ChatRole.User, "Hi")], "claude-3-5-sonnet-20241022", null, CancellationToken.None);

        (await act.Should().ThrowAsync<AiProviderRateLimitedException>()).Which.RetryAfter.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ChatAsync_ShouldThrowAiProviderNotConfiguredException_WhenNoCredentialIsConfigured()
    {
        var uncredentialedProvider = AIProvider.Create("anthropic", "Anthropic", "test");
        _providers.GetByKeyAsync("anthropic", Arg.Any<CancellationToken>()).Returns(uncredentialedProvider);
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK), out _);

        var act = () => provider.ChatAsync([new ChatMessage(ChatRole.User, "Hi")], "claude-3-5-sonnet-20241022", null, CancellationToken.None);

        // specs/043 FR-001: "no credential was ever set" is a different administrator action
        // from "the vendor rejected the credential we sent", so it classifies distinctly.
        await act.Should().ThrowAsync<AiProviderNotConfiguredException>();
    }

    [Fact]
    public async Task StreamChatAsync_ShouldYieldContentDeltasThenFinalUsage()
    {
        const string sse =
            "event: message_start\n" +
            """data: {"type":"message_start","message":{"usage":{"input_tokens":7}}}""" + "\n\n" +
            "event: content_block_delta\n" +
            """data: {"type":"content_block_delta","delta":{"text":"Hello"}}""" + "\n\n" +
            "event: content_block_delta\n" +
            """data: {"type":"content_block_delta","delta":{"text":" world"}}""" + "\n\n" +
            "event: message_delta\n" +
            """data: {"type":"message_delta","usage":{"output_tokens":3}}""" + "\n\n" +
            "event: message_stop\n" +
            """data: {"type":"message_stop"}""" + "\n\n";

        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse, Encoding.UTF8, "text/event-stream"),
        }, out _);

        var chunks = new List<StreamChunk>();
        await foreach (var chunk in provider.StreamChatAsync(
            [new ChatMessage(ChatRole.User, "Hi")], "claude-3-5-sonnet-20241022", null, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        chunks.Where(c => c.ContentDelta is not null).Select(c => c.ContentDelta).Should().Equal("Hello", " world");
        var finalChunk = chunks.Should().ContainSingle(c => c.Usage != null).Subject;
        finalChunk.Usage!.InputTokenCount.Should().Be(7);
        finalChunk.Usage!.OutputTokenCount.Should().Be(3);
    }

    [Fact]
    public async Task ListAvailableModelsAsync_ShouldFollowPagination_AcrossEveryPage()
    {
        // specs/043 FR-028a - Anthropic pages this endpoint with has_more/after_id.
        var callCount = 0;
        var provider = CreateProvider(_ =>
        {
            callCount++;
            var json = callCount == 1
                ? """{"data":[{"id":"claude-a","display_name":"A"}],"has_more":true}"""
                : """{"data":[{"id":"claude-b","display_name":"B"}],"has_more":false}""";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }, out _);

        var models = await provider.ListAvailableModelsAsync(CancellationToken.None);

        callCount.Should().Be(2);
        models.Select(m => m.ModelKey).Should().BeEquivalentTo(["claude-a", "claude-b"]);
    }

    [Fact]
    public async Task ListAvailableModelsAsync_ShouldReportAbsentTokenLimits_AsNull()
    {
        // specs/043 FR-029: Anthropic's list carries no token metadata. Substituting 0 made
        // every one of these rows unaddable.
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"data":[{"id":"claude-a","display_name":"A"}],"has_more":false}""", Encoding.UTF8, "application/json"),
        }, out _);

        var models = await provider.ListAvailableModelsAsync(CancellationToken.None);

        models.Should().ContainSingle(m => m.ContextWindowTokens == null && m.MaxOutputTokens == null);
    }
}
