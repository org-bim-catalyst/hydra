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
    public async Task ChatAsync_ShouldThrowAiProviderUsageRestrictedException_When403WithNoVendorReason()
    {
        var provider = CreateProvider(
            _ => new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{}") }, out _);

        var act = () => provider.ChatAsync([new ChatMessage(ChatRole.User, "Hi")], "gemini-1.5-pro", null, CancellationToken.None);

        // specs/043 FR-002: Google returns 403 for an invalid key, a disabled API, and disabled
        // billing alike. With no vendor reason to separate them, "restricted" is the honest
        // answer - the previous behaviour told an administrator to check an API key that may
        // well be valid, which is the misdirection this feature exists to remove.
        await act.Should().ThrowAsync<AiProviderUsageRestrictedException>();
    }

    [Fact]
    public async Task ChatAsync_ShouldThrowAiProviderAuthenticationException_When403SaysTheKeyIsInvalid()
    {
        const string body = """{"error":{"status":"INVALID_ARGUMENT","details":[{"reason":"API_KEY_INVALID"}]}}""";
        var provider = CreateProvider(
            _ => new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent(body) }, out _);

        var act = () => provider.ChatAsync([new ChatMessage(ChatRole.User, "Hi")], "gemini-1.5-pro", null, CancellationToken.None);

        // The vendor reason wins over the status: same 403, different classification.
        await act.Should().ThrowAsync<AiProviderAuthenticationException>();
    }

    [Fact]
    public async Task ChatAsync_ShouldThrowAiProviderUsageRestrictedException_When403SaysBillingIsDisabled()
    {
        const string body = """{"error":{"status":"PERMISSION_DENIED","details":[{"reason":"BILLING_DISABLED"}]}}""";
        var provider = CreateProvider(
            _ => new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent(body) }, out _);

        var act = () => provider.ChatAsync([new ChatMessage(ChatRole.User, "Hi")], "gemini-1.5-pro", null, CancellationToken.None);

        await act.Should().ThrowAsync<AiProviderUsageRestrictedException>();
    }

    [Fact]
    public async Task ChatAsync_ShouldThrowAiProviderNotConfiguredException_WhenNoCredentialIsConfigured()
    {
        var uncredentialedProvider = AIProvider.Create("google-gemini", "Google Gemini", "test");
        _providers.GetByKeyAsync("google-gemini", Arg.Any<CancellationToken>()).Returns(uncredentialedProvider);
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK), out _);

        var act = () => provider.ChatAsync([new ChatMessage(ChatRole.User, "Hi")], "gemini-1.5-pro", null, CancellationToken.None);

        // specs/043 FR-001: "no credential was ever set" is a different administrator action
        // from "the vendor rejected the credential we sent", so it classifies distinctly.
        await act.Should().ThrowAsync<AiProviderNotConfiguredException>();
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
    public async Task ListAvailableModelsAsync_ShouldKeepANullTokenLimit_AsNull_NotThrow()
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
        // specs/043 FR-029: absent stays absent. Substituting 0 here is what made every such
        // row fail the catalog's own validation, so none of them could ever be added.
        models.Should().ContainSingle(m => m.ModelKey == "text-embedding-004" && m.ContextWindowTokens == null && m.MaxOutputTokens == null);
    }

    [Fact]
    public async Task ListAvailableModelsAsync_ShouldFollowPagination_AcrossEveryPage()
    {
        // specs/043 FR-028a: Gemini paginates this endpoint. Reading only the first page
        // silently omitted models while the diff still looked successful - the worst kind of
        // failure, because nothing tells the administrator anything is missing.
        var callCount = 0;
        var provider = CreateProvider(_ =>
        {
            callCount++;
            var json = callCount == 1
                ? """{"models":[{"name":"models/gemini-a","displayName":"A","inputTokenLimit":1000,"outputTokenLimit":100}],"nextPageToken":"page-2"}"""
                : """{"models":[{"name":"models/gemini-b","displayName":"B","inputTokenLimit":2000,"outputTokenLimit":200}]}""";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }, out _);

        var models = await provider.ListAvailableModelsAsync(CancellationToken.None);

        callCount.Should().Be(2);
        models.Should().HaveCount(2);
        models.Select(m => m.ModelKey).Should().BeEquivalentTo(["gemini-a", "gemini-b"]);
    }

    [Fact]
    public async Task ListAvailableModelsAsync_ShouldStop_WhenAContinuationNeverTerminates()
    {
        // A page token that always points at another page must become a classified failure,
        // not an unbounded loop holding an admin request open.
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"models":[{"name":"models/loop"}],"nextPageToken":"always-more"}""", Encoding.UTF8, "application/json"),
        }, out _);

        var act = () => provider.ListAvailableModelsAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<AiProviderResponseInvalidException>())
            .Which.Kind.Should().Be(AiProviderFailureKind.ResponseNotUnderstood);
    }

    [Fact]
    public async Task ListAvailableModelsAsync_ShouldClassifyAnUndecryptableCredential_AsCredentialUnreadable()
    {
        // specs/043 FR-004 - the reported bug's most likely root cause. This escaped as a raw
        // CryptographicException and surfaced as "An unexpected error occurred."
        _credentialProtector.Unprotect("ciphertext").Returns(_ => throw new System.Security.Cryptography.CryptographicException("key ring changed"));
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK), out _);

        var act = () => provider.ListAvailableModelsAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<AiProviderCredentialUnreadableException>())
            .Which.Kind.Should().Be(AiProviderFailureKind.CredentialUnreadable);
    }

    [Fact]
    public async Task ListAvailableModelsAsync_ShouldClassifyAnUnparseableBody_AsResponseNotUnderstood()
    {
        // FR-006 - previously a raw JsonException, i.e. a generic 500.
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not json at all", Encoding.UTF8, "application/json"),
        }, out _);

        var act = () => provider.ListAvailableModelsAsync(CancellationToken.None);

        await act.Should().ThrowAsync<AiProviderResponseInvalidException>();
    }

    [Fact]
    public async Task ListAvailableModelsAsync_ShouldClassifyATimeout_AsUnavailable()
    {
        // FR-005 - previously a raw TaskCanceledException, i.e. a generic 500.
        var provider = CreateProvider(_ => throw new TaskCanceledException("timed out"), out _);

        var act = () => provider.ListAvailableModelsAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<AiProviderUnavailableException>())
            .Which.Kind.Should().Be(AiProviderFailureKind.Unavailable);
    }

    [Fact]
    public async Task ListAvailableModelsAsync_ShouldPropagateACallerCancellation_AsCancellation()
    {
        // FR-035: a user action is not a provider failure.
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var provider = CreateProvider(_ => throw new TaskCanceledException("caller cancelled"), out _);

        var act = () => provider.ListAvailableModelsAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CheckHealthAsync_ShouldReportTheClassification_RatherThanABareFalse()
    {
        // specs/043 FR-016: the reason is what turns an unexplained red chip into something an
        // administrator can act on.
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent(
                """{"error":{"status":"RESOURCE_EXHAUSTED","details":[{"@type":"type.googleapis.com/google.rpc.QuotaFailure"}]}}""",
                Encoding.UTF8, "application/json"),
        }, out _);

        var result = await provider.CheckHealthAsync(CancellationToken.None);

        result.IsHealthy.Should().BeFalse();
        result.Kind.Should().Be(AiProviderFailureKind.QuotaExhausted);
        result.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CheckHealthAsync_ShouldReportHealthy_WhenTheModelsCallSucceeds()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"models":[]}""", Encoding.UTF8, "application/json"),
        }, out _);

        var result = await provider.CheckHealthAsync(CancellationToken.None);

        result.IsHealthy.Should().BeTrue();
        result.Kind.Should().BeNull();
    }
}
