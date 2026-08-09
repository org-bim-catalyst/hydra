using System.Diagnostics;
using AskLucy.Domain.Memory;
using AskLucy.Domain.Retrieval;
using AskLucy.Persistence.Memory;
using AskLucy.Persistence.Repositories;
using FluentAssertions;
using MemoryEntity = AskLucy.Domain.Memory.Memory;

namespace AskLucy.Persistence.Tests.Memory;

/// <summary>
/// tasks.md T103a (spec.md SC-006: "no perceptible delay ... even for accounts with thousands
/// of stored memories") — a real-SQL-Server-only regression guard (constitution &#167;10) for the
/// two database round trips <see cref="IMemoryService.RetrieveRelevantMemoriesAsync"/> actually
/// performs against the row count SC-006 names: <see cref="SqlServerMemoryVectorStore.QueryNearestAsync"/>
/// (the <c>VECTOR_DISTANCE</c> scan) and <see cref="MemoryRepository.GetActiveByIdsAsync"/>
/// (materializing the top-K candidates).
///
/// <para>Deliberately placed here rather than in <c>AskLucy.Infrastructure.Tests</c> as
/// tasks.md's template phrasing suggested, and deliberately does not call
/// <c>MemoryService.RetrieveRelevantMemoriesAsync</c> directly — a discovered scope
/// correction, mirroring the one already recorded on <c>MemoryExtractionRetryTests</c>.
/// <see cref="AskLucy.Application.Memory.MemoryService"/>'s own in-memory ranking step
/// (<c>RankCandidate</c>/<c>SelectWithinBudget</c>) only ever iterates the vector store's
/// already-topK-limited result set (at most 8 rows), so it can never be the bottleneck at
/// scale — the row count in SC-006 only stresses the SQL Server query itself, which requires
/// a real instance (constitution &#167;10 forbids an in-memory fake here) and is not reachable
/// without also faking out the embedding provider AI call that
/// <c>RetrieveRelevantMemoriesAsync</c> makes first.</para>
/// </summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class MemoryRetrievalPerformanceTests(PersistenceTestFixture fixture)
{
    private const int MemoryCount = 3_000;
    private const int TopK = 8;

    [Fact]
    public async Task QueryNearestAsync_ThenGetActiveByIdsAsync_ShouldCompleteWellUnderAPerceptibleDelay_At3000StoredMemories()
    {
        var userId = $"owner-{Guid.NewGuid():N}";
        var random = new Random(42);
        var provider = EmbeddingProvider.Create("openai", "text-embedding-3-small", MemoryEmbedding.VectorWidth, EmbeddingHostingType.Cloud, isDefault: true, "test");

        var memories = Enumerable.Range(0, MemoryCount)
            .Select(i => MemoryEntity.CreateCandidate(
                userId, null, MemoryCategory.PersonalFact, $"Fact {i}", MemorySourceType.PassiveConversationAnalysis,
                null, 0.5m, 0.5m, isSensitive: false, MemoryApprovalMode.Automatic, "test"))
            .ToList();
        var embeddings = memories
            .Select(m => MemoryEmbedding.Create(m.Id, provider.Id, RandomVector(random), "test"))
            .ToList();

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(PersistenceTestFixture.CreateTestUser(userId));
            seedContext.EmbeddingProviders.Add(provider);
            seedContext.Memories.AddRange(memories);
            seedContext.MemoryEmbeddings.AddRange(embeddings);
            await seedContext.SaveChangesAsync();

            var vectorStore = new SqlServerMemoryVectorStore(seedContext);
            foreach (var embedding in embeddings)
            {
                await vectorStore.UpsertAsync(embedding.MemoryId, embedding.Id, embedding.Vector, CancellationToken.None);
            }
        }

        var queryVector = RandomVector(random);

        await using var readContext = fixture.CreateDbContext();
        var readVectorStore = new SqlServerMemoryVectorStore(readContext);
        var memoryRepository = new MemoryRepository(readContext);

        var stopwatch = Stopwatch.StartNew();
        var candidates = await readVectorStore.QueryNearestAsync(queryVector, userId, null, TopK, 0.0, CancellationToken.None);
        var activeMemories = await memoryRepository.GetActiveByIdsAsync(candidates.Select(c => c.MemoryId).ToList(), CancellationToken.None);
        stopwatch.Stop();

        candidates.Should().HaveCount(TopK);
        activeMemories.Should().HaveCount(TopK);
        stopwatch.Elapsed.Should().BeLessThan(
            TimeSpan.FromSeconds(2), "SC-006: memory retrieval must add no perceptible delay even at thousands of stored memories");
    }

    private static float[] RandomVector(Random random) =>
        Enumerable.Range(0, MemoryEmbedding.VectorWidth).Select(_ => (float)random.NextDouble()).ToArray();
}
