using System.Net;
using System.Text;
using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;
using AskLucy.Infrastructure.Retrieval.Embeddings;
using AskLucy.Infrastructure.Tests.Ai;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Retrieval;

/// <summary>
/// Which key the embedding calls authenticate with.
/// </summary>
/// <remarks>
/// This provider used to read <c>OpenAiEmbedding:ApiKey</c> from configuration and nothing else.
/// That section is set in neither appsettings.json nor appsettings.Production.json, so in
/// production the key was empty and every call came back 401 — memory retrieval failed silently on
/// every single turn, for weeks, while chat read the same vendor's credential from the database
/// and worked fine. Configuring OpenAI on the admin page is the thing an administrator actually
/// does, so that credential is what this uses now.
/// </remarks>
public sealed class OpenAiEmbeddingProviderTests
{
    private readonly IAIProviderRepository _providers = Substitute.For<IAIProviderRepository>();
    private readonly IAiCredentialProtector _protector = Substitute.For<IAiCredentialProtector>();

    private OpenAiEmbeddingProvider CreateProvider(out List<string> authorizationHeaders)
    {
        var captured = new List<string>();
        authorizationHeaders = captured;

        var handler = new StubHttpMessageHandler(request =>
        {
            captured.Add(request.Headers.Authorization?.Parameter ?? "(none)");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"data":[{"embedding":[0.1,0.2]}]}""", Encoding.UTF8, "application/json"),
            };
        });

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("OpenAiEmbedding").Returns(new HttpClient(handler));

        return new OpenAiEmbeddingProvider(
            factory,
            Options.Create(new OpenAiEmbeddingOptions { ApiKey = "key-from-configuration" }),
            _providers,
            _protector);
    }

    private void UseConfiguredProvider(string? ciphertext)
    {
        var provider = AIProvider.Create("openai", "OpenAI", "test");
        if (ciphertext is not null)
        {
            provider.SetCredential(ciphertext, "test");
            _protector.Unprotect(ciphertext).Returns("key-from-the-admin-page");
        }

        _providers.GetByKeyAsync("openai", Arg.Any<CancellationToken>()).Returns(provider);
    }

    [Fact]
    public async Task EmbedBatchAsync_ShouldAuthenticateWithTheCredentialAnAdministratorConfigured()
    {
        UseConfiguredProvider("ciphertext");
        var provider = CreateProvider(out var authorizationHeaders);

        await provider.EmbedBatchAsync(["hello"], TestContext.Current.CancellationToken);

        authorizationHeaders.Should().ContainSingle().Which.Should().Be("key-from-the-admin-page");
    }

    [Fact]
    public async Task EmbedBatchAsync_ShouldFallBackToConfiguration_WhenNoProviderCredentialIsSet()
    {
        UseConfiguredProvider(ciphertext: null);
        var provider = CreateProvider(out var authorizationHeaders);

        await provider.EmbedBatchAsync(["hello"], TestContext.Current.CancellationToken);

        authorizationHeaders.Should().ContainSingle().Which.Should().Be("key-from-configuration");
    }

    /// <summary>
    /// An unreadable credential is a real fault worth naming — the ephemeral Data Protection key
    /// ring made exactly this happen once — not a reason to quietly try a key that is almost
    /// certainly absent.
    /// </summary>
    [Fact]
    public async Task EmbedBatchAsync_ShouldFail_RatherThanFallBack_WhenTheCredentialCannotBeDecrypted()
    {
        var provider = AIProvider.Create("openai", "OpenAI", "test");
        provider.SetCredential("corrupt", "test");
        _providers.GetByKeyAsync("openai", Arg.Any<CancellationToken>()).Returns(provider);
        _protector.Unprotect("corrupt").Throws(new System.Security.Cryptography.CryptographicException("bad key ring"));

        var embedding = CreateProvider(out var authorizationHeaders);

        var act = async () => await embedding.EmbedBatchAsync(["hello"], TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<AiProviderCredentialUnreadableException>();
        authorizationHeaders.Should().BeEmpty();
    }
}
