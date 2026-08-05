using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Domain.KnowledgeBases;
using AskLucy.Infrastructure.Retrieval.VectorStores;
using AskLucy.Infrastructure.Tests.Ai;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Retrieval;

/// <summary>ADR-0007 — proves <see cref="PineconeVectorStore"/> builds correct request payloads/headers, maps responses to <see cref="VectorSearchCandidate"/>, and maps HTTP outcomes to the shared provider-exception types, against recorded/replayed HTTP responses.</summary>
public sealed class PineconeVectorStoreTests
{
    private static readonly Guid DocumentChunkId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EmbeddingId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid KnowledgeBaseId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static PineconeVectorStore CreateStore(
        Func<HttpRequestMessage, HttpResponseMessage> responder, out StubHttpMessageHandler handler)
    {
        handler = new StubHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://test-index.svc.pinecone.io/") };
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("Pinecone").Returns(httpClient);

        return new PineconeVectorStore(factory);
    }

    [Fact]
    public void Provider_ShouldBePinecone()
    {
        var store = CreateStore(_ => new HttpResponseMessage(HttpStatusCode.OK), out _);

        store.Provider.Should().Be(VectorStoreProvider.Pinecone);
    }

    [Fact]
    public async Task UpsertAsync_ShouldSendCompositeIdAndMetadata()
    {
        var store = CreateStore(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") }, out var handler);

        await store.UpsertAsync(DocumentChunkId, EmbeddingId, KnowledgeBaseId, [0.1f, 0.2f], CancellationToken.None);

        handler.LastRequest!.RequestUri!.ToString().Should().Contain("vectors/upsert");

        var body = await handler.LastRequest.Content!.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var vector = document.RootElement.GetProperty("vectors")[0];

        vector.GetProperty("id").GetString().Should().Be($"{DocumentChunkId:N}:{EmbeddingId:N}");
        vector.GetProperty("metadata").GetProperty("documentChunkId").GetString().Should().Be(DocumentChunkId.ToString("D"));
        vector.GetProperty("metadata").GetProperty("knowledgeBaseId").GetString().Should().Be(KnowledgeBaseId.ToString("D"));
    }

    [Fact]
    public async Task QueryNearestAsync_ShouldSendInFilter_AndMapMatchesToCandidates()
    {
        var responsePayload = new
        {
            matches = new[]
            {
                new { id = "a", score = 0.9, metadata = new { documentChunkId = DocumentChunkId.ToString("D") } },
            },
        };

        var store = CreateStore(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(responsePayload),
        }, out var handler);

        var results = await store.QueryNearestAsync([0.1f, 0.2f], [KnowledgeBaseId], topK: 5, similarityThreshold: 0.5, CancellationToken.None);

        handler.LastRequest!.RequestUri!.ToString().Should().Contain("query");
        var body = await handler.LastRequest.Content!.ReadAsStringAsync();
        body.Should().Contain("$in").And.Contain(KnowledgeBaseId.ToString("D"));

        results.Should().ContainSingle();
        results[0].DocumentChunkId.Should().Be(DocumentChunkId);
        results[0].Distance.Should().BeApproximately(0.1, 0.0001); // Distance = 1 - score
    }

    [Fact]
    public async Task QueryNearestAsync_ShouldFilterOutMatchesBelowSimilarityThreshold()
    {
        var responsePayload = new
        {
            matches = new[]
            {
                new { id = "a", score = 0.2, metadata = new { documentChunkId = DocumentChunkId.ToString("D") } },
            },
        };

        var store = CreateStore(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(responsePayload),
        }, out _);

        // score 0.2 -> distance 0.8, which exceeds maxDistance (1 - 0.5 = 0.5)
        var results = await store.QueryNearestAsync([0.1f, 0.2f], [KnowledgeBaseId], topK: 5, similarityThreshold: 0.5, CancellationToken.None);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_ShouldListByPrefix_ThenDeleteReturnedIds()
    {
        var store = CreateStore(request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri!.ToString().Contains("vectors/list"))
            {
                var listPayload = new { vectors = new[] { new { id = $"{DocumentChunkId:N}:{EmbeddingId:N}" } } };
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(listPayload) };
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }, out var handler);

        await store.DeleteAsync(DocumentChunkId, CancellationToken.None);

        handler.LastRequest!.RequestUri!.ToString().Should().Contain("vectors/delete");
        var body = await handler.LastRequest.Content!.ReadAsStringAsync();
        body.Should().Contain($"{DocumentChunkId:N}:{EmbeddingId:N}");
    }

    [Fact]
    public async Task DeleteAsync_ShouldNotCallDelete_WhenListReturnsNoVectors()
    {
        var deleteCalled = false;
        var store = CreateStore(request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { vectors = Array.Empty<object>() }) };
            }

            deleteCalled = true;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }, out _);

        await store.DeleteAsync(DocumentChunkId, CancellationToken.None);

        deleteCalled.Should().BeFalse();
    }

    [Fact]
    public async Task UpsertAsync_ShouldThrowAiProviderAuthenticationException_When401()
    {
        var store = CreateStore(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{}") }, out _);

        var act = () => store.UpsertAsync(DocumentChunkId, EmbeddingId, KnowledgeBaseId, [0.1f], CancellationToken.None);

        await act.Should().ThrowAsync<AiProviderAuthenticationException>();
    }

    [Fact]
    public async Task UpsertAsync_ShouldThrowAiProviderRateLimitedException_When429()
    {
        var store = CreateStore(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("{}") }, out _);

        var act = () => store.UpsertAsync(DocumentChunkId, EmbeddingId, KnowledgeBaseId, [0.1f], CancellationToken.None);

        await act.Should().ThrowAsync<AiProviderRateLimitedException>();
    }

    [Fact]
    public async Task UpsertAsync_ShouldThrowAiProviderUnavailableException_OnOtherFailures()
    {
        var store = CreateStore(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("boom") }, out _);

        var act = () => store.UpsertAsync(DocumentChunkId, EmbeddingId, KnowledgeBaseId, [0.1f], CancellationToken.None);

        await act.Should().ThrowAsync<AiProviderUnavailableException>();
    }
}
