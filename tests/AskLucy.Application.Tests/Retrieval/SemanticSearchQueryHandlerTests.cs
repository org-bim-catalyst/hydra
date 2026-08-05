using AskLucy.Application.Abstractions;
using AskLucy.Application.Retrieval;
using AskLucy.Application.Retrieval.Queries.SemanticSearch;
using AskLucy.Domain.KnowledgeBases;
using AskLucy.Domain.Retrieval;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Retrieval;

/// <summary>
/// ADR-0007 — proves the vector-store partition-and-merge logic: when the resolved knowledge
/// bases span more than one <see cref="VectorStoreProvider"/>, results from BOTH providers must
/// survive into the final ranked list. Silently searching only one provider's knowledge bases
/// would drop the other provider's results with no error — the exact regression this test guards
/// against.
/// </summary>
public sealed class SemanticSearchQueryHandlerTests
{
    private readonly IKnowledgeBaseRepository _knowledgeBaseRepository = Substitute.For<IKnowledgeBaseRepository>();
    private readonly IEmbeddingProviderRepository _embeddingProviderRepository = Substitute.For<IEmbeddingProviderRepository>();
    private readonly IEmbeddingServiceResolver _embeddingServiceResolver = Substitute.For<IEmbeddingServiceResolver>();
    private readonly IVectorStoreResolver _vectorStoreResolver = Substitute.For<IVectorStoreResolver>();
    private readonly IDocumentChunkRepository _documentChunkRepository = Substitute.For<IDocumentChunkRepository>();
    private readonly IKnowledgeBaseDocumentRepository _knowledgeBaseDocumentRepository = Substitute.For<IKnowledgeBaseDocumentRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly SemanticSearchQueryHandler _handler;

    public SemanticSearchQueryHandlerTests()
    {
        _currentUser.UserId.Returns("user-1");

        var enricher = new SearchResultEnricher(_documentChunkRepository, _knowledgeBaseRepository, _knowledgeBaseDocumentRepository);

        _handler = new SemanticSearchQueryHandler(
            _knowledgeBaseRepository, _embeddingProviderRepository, _embeddingServiceResolver, _vectorStoreResolver, enricher, _currentUser);
    }

    [Fact]
    public async Task Handle_ShouldMergeResultsFromBothVectorStores_WhenKnowledgeBasesSpanBothProviders()
    {
        var sqlServerKb = KnowledgeBase.Create("SQL KB", "user-1", "user-1");
        sqlServerKb.UpdateRetrievalSettings(
            ChunkingStrategy.Recursive, null, EmbeddingHostingType.Cloud, VectorStoreProvider.SqlServer, requiresDataResidency: false, "user-1");

        var pineconeKb = KnowledgeBase.Create("Pinecone KB", "user-1", "user-1");
        // VectorStoreProvider already defaults to Pinecone on Create.

        _knowledgeBaseRepository.ResolveOwnedIdsAsync(
            "user-1", Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<Guid>>([sqlServerKb.Id, pineconeKb.Id]);

        _knowledgeBaseRepository.GetByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<KnowledgeBase>>([sqlServerKb, pineconeKb]);

        _knowledgeBaseRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(sqlServerKb);

        var defaultProvider = EmbeddingProvider.Create("OpenAI", "text-embedding-3-small", 2, EmbeddingHostingType.Cloud, isDefault: true, "system");
        _embeddingProviderRepository.GetDefaultAsync(EmbeddingHostingType.Cloud, Arg.Any<CancellationToken>()).Returns(defaultProvider);

        var embeddingService = Substitute.For<IEmbeddingService>();
        embeddingService.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new EmbeddingResult([0.1f, 0.2f], 2));
        _embeddingServiceResolver.Resolve("OpenAI").Returns(embeddingService);

        var sqlChunk = DocumentChunk.Create(
            sqlServerKb.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ChunkingStrategy.Recursive,
            "sql content", "hash-sql", 10, 50, null, null, null, null, 0, "system");

        var pineconeChunk = DocumentChunk.Create(
            pineconeKb.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ChunkingStrategy.Recursive,
            "pinecone content", "hash-pinecone", 10, 50, null, null, null, null, 0, "system");

        var sqlStore = Substitute.For<IVectorStore>();
        sqlStore.Provider.Returns(VectorStoreProvider.SqlServer);
        sqlStore.QueryNearestAsync(Arg.Any<float[]>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<int>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<VectorSearchCandidate>>([new VectorSearchCandidate(sqlChunk.Id, 0.1)]);

        var pineconeStore = Substitute.For<IVectorStore>();
        pineconeStore.Provider.Returns(VectorStoreProvider.Pinecone);
        pineconeStore.QueryNearestAsync(Arg.Any<float[]>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<int>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<VectorSearchCandidate>>([new VectorSearchCandidate(pineconeChunk.Id, 0.2)]);

        _vectorStoreResolver.Resolve(VectorStoreProvider.SqlServer).Returns(sqlStore);
        _vectorStoreResolver.Resolve(VectorStoreProvider.Pinecone).Returns(pineconeStore);

        _documentChunkRepository.GetByIdAsync(sqlChunk.Id, Arg.Any<CancellationToken>()).Returns(sqlChunk);
        _documentChunkRepository.GetByIdAsync(pineconeChunk.Id, Arg.Any<CancellationToken>()).Returns(pineconeChunk);

        var result = await _handler.Handle(
            new SemanticSearchQuery("query text", [sqlServerKb.Id, pineconeKb.Id], null, TopK: 10, SimilarityThreshold: 0.0m),
            CancellationToken.None);

        result.Should().HaveCount(2, "results from BOTH vector stores must survive the merge, not just the first-queried provider's");
        result.Select(r => r.ChunkId).Should().BeEquivalentTo([sqlChunk.Id, pineconeChunk.Id]);
        // sqlChunk's distance (0.1) is smaller/closer than pineconeChunk's (0.2), so it ranks first.
        result[0].ChunkId.Should().Be(sqlChunk.Id);
    }
}
