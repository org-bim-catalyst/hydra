using System.Globalization;
using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Domain.KnowledgeBases;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Retrieval;

/// <summary>
/// One of two <see cref="IVectorStore"/> implementations as of ADR-0007 (the other being
/// <c>PineconeVectorStore</c>) — SQL Server's native <c>vector(n)</c> column, queried via
/// <c>VECTOR_DISTANCE</c>. Selected per knowledge base via
/// <see cref="AskLucy.Domain.KnowledgeBases.KnowledgeBase.VectorStoreProvider"/>; mandatory for
/// knowledge bases with <c>RequiresDataResidency</c> set, and the backfilled default for every
/// knowledge base that existed before ADR-0007 (research.md Decision 3).
///
/// <para><b>Located in <c>AskLucy.Persistence</c>, not <c>AskLucy.Infrastructure</c></b>
/// (a deviation from plan.md's originally-proposed file path, discovered during
/// <c>/speckit-implement</c>): this store needs the exact same <see cref="AskLucyDbContext"/>
/// connection/schema EF Core manages, and <c>AskLucy.Infrastructure</c> has no project reference
/// to <c>AskLucy.Persistence</c> (they are sibling projects per constitution §3) — that reference
/// would be the wrong direction to introduce. <c>AskLucy.Persistence</c> already plays the
/// "EF Core/database infrastructure" role for this solution, so it is the correct home for a
/// raw-SQL companion to <c>EmbeddingConfiguration</c>'s Ignore'd <c>Vector</c> property.</para>
///
/// <para>The vector itself is passed as a JSON array string and cast server-side
/// (<c>CAST(@json AS VECTOR(n))</c>) rather than bound as a typed ADO.NET parameter; every value
/// is still fully parameterized (constitution §8 — no string-interpolated user input reaches SQL
/// text) except <c>n</c> itself, which SQL Server requires as a literal in the type declaration —
/// <c>CAST(@p0 AS VECTOR(@p1))</c> fails with "Incorrect syntax near '@p1'" (caught 2026-08-18: no
/// automated test exercised <see cref="UpsertAsync"/> against a real instance, so this had shipped
/// broken since specs/016; <c>QueryNearestAsync</c>'s already-correct literal-width string
/// concatenation below is now mirrored in <see cref="UpsertAsync"/> too). Knowledge base ids are
/// validated <see cref="Guid"/> values, never raw user input, before being inlined into the
/// <c>IN (...)</c> clause.</para>
/// </summary>
public sealed class SqlServerVectorStore(AskLucyDbContext dbContext) : IVectorStore
{
    public VectorStoreProvider Provider => VectorStoreProvider.SqlServer;

    public async Task UpsertAsync(Guid documentChunkId, Guid embeddingId, Guid knowledgeBaseId, float[] vector, CancellationToken cancellationToken = default)
    {
        // knowledgeBaseId is unused here — this store already scopes by KnowledgeBaseId at query
        // time via its join to DocumentChunks (see QueryNearestAsync), not at upsert time. The
        // parameter exists on IVectorStore because PineconeVectorStore needs it as vector metadata
        // (ADR-0007).
        var vectorJson = ToJsonArray(vector);

        // VECTOR(n)'s width must be a literal in the type declaration — SQL Server rejects
        // CAST(@p0 AS VECTOR(@p1)) with "Incorrect syntax near '@p1'". Interpolating it via
        // ExecuteSqlInterpolatedAsync (as the CAST target's own value) parameterizes it like
        // everything else in the FormattableString, so it has to be baked into the raw SQL text
        // instead — same technique QueryNearestAsync below already uses for its own CAST.
        var sql = "UPDATE [Embeddings] SET [Vector] = CAST({0} AS VECTOR(" +
            Domain.Retrieval.Embedding.VectorWidth.ToString(CultureInfo.InvariantCulture) + ")) WHERE [Id] = {1}";

        await dbContext.Database.ExecuteSqlRawAsync(sql, [vectorJson, embeddingId], cancellationToken);
    }

    public async Task DeleteAsync(Guid documentChunkId, CancellationToken cancellationToken = default)
    {
        // A hard delete of the vector value itself (distinct from the soft-deleted Embedding
        // metadata row, which EF already handles via its own query filter) — frees the column's
        // storage once a chunk is permanently superseded.
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [Embeddings] SET [Vector] = NULL WHERE [DocumentChunkId] = {documentChunkId}",
            cancellationToken);
    }

    public async Task<IReadOnlyList<VectorSearchCandidate>> QueryNearestAsync(
        float[] queryVector, IReadOnlyList<Guid> knowledgeBaseIds, int topK, double similarityThreshold,
        CancellationToken cancellationToken = default)
    {
        if (knowledgeBaseIds.Count == 0)
        {
            return [];
        }

        var vectorJson = ToJsonArray(queryVector);
        var maxDistance = 1.0 - similarityThreshold;
        var knowledgeBaseIdList = string.Join(
            ",", knowledgeBaseIds.Select(id => $"'{id.ToString("D", CultureInfo.InvariantCulture)}'"));

        // Guid values are formatted via the fixed "D" format (no free-form user text ever reaches
        // this string) — the query vector and every other value are passed as real parameters
        // ({0}/{1} below, SqlQueryRaw's positional placeholder syntax).
        var castExpression = "CAST({0} AS VECTOR(" + Domain.Retrieval.Embedding.VectorWidth.ToString(CultureInfo.InvariantCulture) + "))";
        var sql = "SELECT TOP (" + topK.ToString(CultureInfo.InvariantCulture) + ") e.[DocumentChunkId] AS DocumentChunkId, " +
            "VECTOR_DISTANCE('cosine', e.[Vector], " + castExpression + ") AS Distance " +
            "FROM [Embeddings] e " +
            "INNER JOIN [DocumentChunks] c ON c.[Id] = e.[DocumentChunkId] " +
            "WHERE e.[IsCurrent] = 1 " +
            "AND e.[DeletedAtUtc] IS NULL " +
            "AND e.[Vector] IS NOT NULL " +
            "AND c.[DeletedAtUtc] IS NULL " +
            "AND c.[KnowledgeBaseId] IN (" + knowledgeBaseIdList + ") " +
            "AND VECTOR_DISTANCE('cosine', e.[Vector], " + castExpression + ") <= {1} " +
            "ORDER BY Distance ASC";

        var results = await dbContext.Database
            .SqlQueryRaw<VectorDistanceRow>(sql, vectorJson, maxDistance)
            .ToListAsync(cancellationToken);

        return results.Select(r => new VectorSearchCandidate(r.DocumentChunkId, r.Distance)).ToList();
    }

    private static string ToJsonArray(float[] vector) => JsonSerializer.Serialize(vector);

    private sealed class VectorDistanceRow
    {
        public Guid DocumentChunkId { get; init; }

        public double Distance { get; init; }
    }
}
