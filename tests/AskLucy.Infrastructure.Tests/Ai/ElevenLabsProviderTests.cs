using System.Net;
using System.Text;
using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;
using AskLucy.Infrastructure.Ai;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Ai;

/// <summary>ElevenLabs listed under Admin → AI providers: its health check and model catalogue use the
/// provider row's key, and it refuses every conversational call.</summary>
public sealed class ElevenLabsProviderTests
{
    private readonly IAIProviderRepository _providers = Substitute.For<IAIProviderRepository>();
    private readonly IAiCredentialProtector _credentialProtector = Substitute.For<IAiCredentialProtector>();

    public ElevenLabsProviderTests()
    {
        var row = AIProvider.Create(ElevenLabsProvider.ProviderKey, "ElevenLabs", "test", AIProviderKind.Speech);
        row.SetCredential("ciphertext", null, "test");
        _providers.GetByKeyAsync(ElevenLabsProvider.ProviderKey, Arg.Any<CancellationToken>()).Returns(row);
        _credentialProtector.Unprotect("ciphertext").Returns("raw-api-key");
    }

    private ElevenLabsProvider CreateProvider(Func<HttpRequestMessage, HttpResponseMessage> responder, out StubHttpMessageHandler handler)
    {
        handler = new StubHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.elevenlabs.io/v1/") };
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("ElevenLabs").Returns(httpClient);
        return new ElevenLabsProvider(factory, _providers, _credentialProtector, Substitute.For<ILogger<ElevenLabsProvider>>());
    }

    [Fact]
    public async Task ListAvailableModelsAsync_ShouldListTextToSpeechModels_WithTheStoredKey()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                [
                  {"model_id":"eleven_v3","name":"Eleven v3","can_do_text_to_speech":true},
                  {"model_id":"eleven_multilingual_sts_v2","name":"Speech to speech","can_do_text_to_speech":false},
                  {"model_id":"eleven_flash_v2_5","name":"Flash v2.5"}
                ]
                """,
                Encoding.UTF8, "application/json"),
        }, out var handler);

        var models = await provider.ListAvailableModelsAsync(TestContext.Current.CancellationToken);

        models.Select(m => m.ModelKey).Should().Equal("eleven_v3", "eleven_flash_v2_5");
        models[0].DisplayName.Should().Be("Eleven v3");
        models.Should().OnlyContain(m => m.Capabilities.Audio && !m.Capabilities.FunctionCalling);
        handler.LastRequest!.RequestUri.Should().Be(new Uri("https://api.elevenlabs.io/v1/models"));
        handler.LastRequest.Headers.GetValues("xi-api-key").Should().ContainSingle().Which.Should().Be("raw-api-key");
    }

    [Fact]
    public async Task CheckHealthAsync_ShouldReportUnhealthy_WhenTheKeyIsRejected()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{}") }, out _);

        var health = await provider.CheckHealthAsync(TestContext.Current.CancellationToken);

        health.IsHealthy.Should().BeFalse();
        health.Kind.Should().Be(AiProviderFailureKind.CredentialRejected);
    }

    [Fact]
    public async Task ChatAsync_ShouldRefuse_BecauseASpeechProviderCannotAnswerInConversation()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK), out var handler);

        var act = () => provider.ChatAsync([new ChatMessage(ChatRole.User, "Hi")], "eleven_v3", null, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotSupportedException>();
        handler.LastRequest.Should().BeNull();
    }
}
