using System.Globalization;
using System.Text.Json;
using AskLucy.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Memory;

/// <summary>
/// The single <see cref="IMemoryVectorStore"/> implementation (research.md Decision 5) — reuses
/// the same raw-ADO.NET-against-a-native-<c>vector(n)</c>-column technique
/// <c>SqlServerVectorStore</c> (specs/016) already proved against a real SQL Server 2025 instance,
/// against the <c>MemoryEmbeddings</c> table instead of RAG's <c>Embeddings</c> table. No
/// <c>CREATE VECTOR INDEX</c> — same inherited platform constraint (specs/016 research.md
/// Decision 3; brute-force <c>VECTOR_DISTANCE</c> scan, bounded per-user by the join to
/// <c>Memories</c>).
/// </summary>
public sealed class SqlServerMemoryVectorStore(AskLucyDbContext dbContext) : IMemoryVectorStore
{
    public async Task UpsertAsync(Guid memoryId, Guid embeddingId, float[] vector, CancellationToken cancellationToken = default)
    {
        var vectorJson = ToJsonArray(vector);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [MemoryEmbeddings] SET [Vector] = CAST({vectorJson} AS VECTOR({Domain.Memory.MemoryEmbedding.VectorWidth})) WHERE [Id] = {embeddingId}",
            cancellationToken);
    }

    public async Task DeleteAsync(Guid memoryId, CancellationToken cancellationToken = default)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [MemoryEmbeddings] SET [Vector] = NULL WHERE [MemoryId] = {memoryId}",
            cancellationToken);
    }

    public async Task<IReadOnlyList<MemoryVectorSearchCandidate>> QueryNearestAsync(
        float[] queryVector, string userId, Guid? projectId, int topK, double similarityThreshold,
        CancellationToken cancellationToken = default)
    {
        var vectorJson = ToJsonArray(queryVector);
        var maxDistance = 1.0 - similarityThreshold;

        // The project-scoping predicate branches on whether a project is active (FR-002/FR-011,
        // User Story 5): with no active project, only general (ProjectId IS NULL) memories are
        // eligible; with one active, general memories remain eligible alongside that project's own.
        var projectPredicate = projectId is null
            ? "m.[ProjectId] IS NULL "
            : "(m.[ProjectId] IS NULL OR m.[ProjectId] = " + "'" + projectId.Value.ToString("D", CultureInfo.InvariantCulture) + "'" + ") ";

        var castExpression = "CAST({0} AS VECTOR(" + Domain.Memory.MemoryEmbedding.VectorWidth.ToString(CultureInfo.InvariantCulture) + "))";
        var sql = "SELECT TOP (" + topK.ToString(CultureInfo.InvariantCulture) + ") e.[MemoryId] AS MemoryId, " +
            "VECTOR_DISTANCE('cosine', e.[Vector], " + castExpression + ") AS Distance " +
            "FROM [MemoryEmbeddings] e " +
            "INNER JOIN [Memories] m ON m.[Id] = e.[MemoryId] " +
            "WHERE e.[IsCurrent] = 1 " +
            "AND e.[DeletedAtUtc] IS NULL " +
            "AND e.[Vector] IS NOT NULL " +
            "AND m.[DeletedAtUtc] IS NULL " +
            "AND m.[State] = 'Active' " +
            "AND m.[UserId] = {1} " +
            "AND " + projectPredicate +
            "AND VECTOR_DISTANCE('cosine', e.[Vector], " + castExpression + ") <= {2} " +
            "ORDER BY Distance ASC";

        var results = await dbContext.Database
            .SqlQueryRaw<VectorDistanceRow>(sql, vectorJson, userId, maxDistance)
            .ToListAsync(cancellationToken);

        return results.Select(r => new MemoryVectorSearchCandidate(r.MemoryId, r.Distance)).ToList();
    }

    private static string ToJsonArray(float[] vector) => JsonSerializer.Serialize(vector);

    private sealed class VectorDistanceRow
    {
        public Guid MemoryId { get; init; }

        public double Distance { get; init; }
    }
}
