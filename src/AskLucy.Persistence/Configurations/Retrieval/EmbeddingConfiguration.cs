using AskLucy.Domain.Retrieval;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Retrieval;

/// <summary>
/// EF Core mapping for <see cref="Embedding"/>.
///
/// <para><b>Known limitation, discovered during <c>/speckit-implement</c> (Foundational
/// phase):</b> <see cref="Embedding.Vector"/> (a plain <c>float[]</c> in Domain, per constitution
/// &#167;3 Domain purity) is <em>not</em> mapped by EF Core here. `Microsoft.EntityFrameworkCore
/// .SqlServer` 10.0.10's `SqlServerVectorTypeMapping`/`SqlServerVectorTranslator` exist internally,
/// but every attempt to reach them from Fluent API code throws a reproducible
/// <c>NullReferenceException</c> during model finalization:</para>
/// <list type="bullet">
/// <item><c>.Property(e =&gt; e.Vector).HasColumnType("vector(n)")</c> on a <c>float[]</c> — NRE
/// in <c>RelationalTypeMappingSource.FindCollectionMapping</c>.</item>
/// <item><c>.HasConversion(...)</c> to <c>ReadOnlyMemory&lt;float&gt;</c> — rejected outright:
/// "the database provider does not support mapping 'ReadOnlyMemory&lt;float&gt;' properties to
/// 'vector(1536)' columns".</item>
/// <item><c>.PrimitiveCollection(e =&gt; e.Vector).HasColumnType("vector(n)")</c> (with or without
/// <c>HasMaxLength</c>) — same NRE as the first case.</item>
/// <item><c>.PrimitiveCollection(e =&gt; e.Vector).HasMaxLength(n)</c> alone (no explicit
/// <c>HasColumnType</c>) is the only combination that does not crash — but it silently falls back
/// to EF's default JSON-in-<c>nvarchar</c> primitive-collection storage, not a true <c>vector</c>
/// column, defeating indexed nearest-neighbor search entirely.</item>
/// </list>
/// <para>Workaround: <see cref="Embedding.Vector"/> is excluded from the EF model (<c>Ignore</c>
/// below); the actual <c>vector(1536)</c> column and every read/write against it are managed
/// entirely via raw SQL/ADO.NET in <c>Persistence/Retrieval/SqlServerVectorStore.cs</c>
/// (research.md Decision 3) and added to the migration via <c>migrationBuilder.Sql(...)</c> rather
/// than a generated <c>CreateTable</c> column. This still honors constitution &#167;5's
/// native-vector-storage mandate — only the *mapping mechanism* changed, not the storage decision.
/// Revisit once a newer <c>Microsoft.EntityFrameworkCore.SqlServer</c> patch fixes the Fluent API
/// path, at which point this workaround can be removed in favor of ordinary EF-managed mapping.
/// <b>No vector index exists on this column</b> — deliberately, confirmed against the real hosted
/// SQL Server 2025 Test instance that <c>CREATE VECTOR INDEX</c> on non-Azure SQL Server produces
/// the pre-Azure/Fabric index format, which makes the table permanently read-only for DML,
/// incompatible with this engine's incremental-indexing requirement (FR-010/FR-011/US5). See
/// research.md Decision 3 ("Vector index — deliberately not used") for the full finding.</para>
/// </summary>
public sealed class EmbeddingConfiguration : IEntityTypeConfiguration<Embedding>
{
    public void Configure(EntityTypeBuilder<Embedding> builder)
    {
        builder.ToTable("Embeddings");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        // See the type-level remarks above — the Vector column is managed entirely outside EF.
        builder.Ignore(e => e.Vector);

        builder.Property(e => e.IsCurrent).IsRequired();

        builder.Property(e => e.CreatedBy).IsRequired();
        builder.Property(e => e.RowVersion).IsRowVersion();

        builder.HasQueryFilter(e => e.DeletedAtUtc == null);

        builder.HasIndex(e => e.DocumentChunkId);
        builder.HasIndex(e => new { e.DocumentChunkId, e.IsCurrent });
        builder.HasIndex(e => e.EmbeddingProviderId);

        builder.HasOne<DocumentChunk>()
            .WithMany()
            .HasForeignKey(e => e.DocumentChunkId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<EmbeddingProvider>()
            .WithMany()
            .HasForeignKey(e => e.EmbeddingProviderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
