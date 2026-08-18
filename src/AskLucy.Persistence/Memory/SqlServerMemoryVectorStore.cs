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

        // VECTOR(n)'s width must be a literal in the type declaration — SQL Server rejects
        // CAST(@p0 AS VECTOR(@p1)) with "Incorrect syntax near '@p1'". Interpolating it via
        // ExecuteSqlInterpolatedAsync (as the CAST target's own value) parameterizes it like
        // everything else in the FormattableString, so it has to be baked into the raw SQL text
        // instead — same technique QueryNearestAsync below already uses for its own CAST.
        var sql = "UPDATE [MemoryEmbeddings] SET [Vector] = CAST({0} AS VECTOR(" +
            Domain.Memory.MemoryEmbedding.VectorWidth.ToString(CultureInfo.InvariantCulture) + ")) WHERE [Id] = {1}";

        await dbContext.Database.ExecuteSqlRawAsync(sql, [vectorJson, embeddingId], cancellationToken);
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

        // VECTOR_DISTANCE is computed once, in the inner query's SELECT, rather than once there
        // and again in an outer WHERE/ORDER BY (T-SQL can't reference a SELECT-list alias from
        // WHERE) — halves the cosine-distance CPU cost of this deliberately unindexed brute-force
        // scan (SC-006's 2s budget was landing at ~2.3s against this shared-hosting instance with
        // the duplicate computation).
        var castExpression = "CAST({0} AS VECTOR(" + Domain.Memory.MemoryEmbedding.VectorWidth.ToString(CultureInfo.InvariantCulture) + "))";
        var sql = "SELECT TOP (" + topK.ToString(CultureInfo.InvariantCulture) + ") sub.[MemoryId], sub.[Distance] " +
            "FROM (" +
            "SELECT e.[MemoryId] AS MemoryId, " +
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
            ") sub " +
            "WHERE sub.[Distance] <= {2} " +
            "ORDER BY sub.[Distance] ASC";

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
