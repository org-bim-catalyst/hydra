using AskLucy.Application.Abstractions;
using AskLucy.Domain.Retrieval;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AskLucy.Web.DevSeed;

/// <summary>
/// Dev-only convenience, mirroring <see cref="DevAiProviderSeeder"/>'s exact shape: restores the
/// baseline <see cref="EmbeddingProvider"/> rows the <c>AddRetrievalEngine</c> migration seeds, if
/// they are missing.
///
/// <para>
/// Why a runtime seeder is needed for rows a migration already seeds: this development database is
/// shared with the persistence test suite, whose <c>PersistenceTestFixture</c> deletes every
/// EF-model table between runs while deliberately preserving <c>__EFMigrationsHistory</c> (so the
/// maintainer's migration record survives). That combination permanently strands migration-seeded
/// reference data — the rows are gone, but EF Core will never re-run an already-recorded migration
/// to put them back. The symptom is an <see cref="InvalidOperationException"/> ("No default
/// embedding provider is configured") from every memory-extraction, memory-conflict-detection and
/// semantic-search path, since all of them resolve the default Cloud provider first.
/// </para>
///
/// <para>
/// Does nothing once any provider row exists, so it never fights an administrator's own
/// configuration — the same early-out <see cref="DevAiProviderSeeder"/> uses. New identifiers are
/// generated rather than reusing the migration's well-known GUIDs: the same table wipe removes
/// every row that could reference them (Embeddings, MemoryEmbeddings, KnowledgeBases), so there is
/// nothing left to match, and reusing them would require a domain factory overload that exists
/// only for this dev concern.
/// </para>
///
/// <para>
/// Known, deliberately out of scope: <c>KnowledgeBaseCategories</c>' 8 predefined rows (seeded by
/// the <c>AddKnowledgeBaseManagement</c> migration) are stranded by the exact same mechanism. Noted
/// here rather than silently accepted — restoring those belongs with the knowledge-base feature,
/// not with the embedding-provider fix this seeder exists for.
/// </para>
/// </summary>
public static class DevBaselineSeeder
{
    private const string SeedActor = "dev-seed";

    public static async Task SeedAsync(IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        var providers = scope.ServiceProvider.GetRequiredService<IEmbeddingProviderRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        if ((await providers.GetActiveAsync()).Count > 0)
        {
            return;
        }

        // Values mirror the AddRetrievalEngine migration's own seed exactly (research.md Decision
        // 5): one Cloud default and one Local default, so a knowledge base's EmbeddingProviderId
        // resolves to a real row and FR-009a's data-residency requirement has a Local option.
        providers.Add(EmbeddingProvider.Create(
            "OpenAI", "text-embedding-3-small", 1536, EmbeddingHostingType.Cloud, isDefault: true, SeedActor));
        providers.Add(EmbeddingProvider.Create(
            "Local", "onnx-minilm-l6-v2", 384, EmbeddingHostingType.Local, isDefault: true, SeedActor));

        await unitOfWork.SaveChangesAsync();

        DevSeedLog.EmbeddingProvidersReseeded(logger);
    }
}

/// <summary>Partial continuation of the <c>DevSeedLog</c> declared in <see cref="DevAdminSeeder"/>'s
/// own file — same namespace, same class, merged by the compiler — rather than a third logging
/// class for what is conceptually the same "dev-only startup seeding" concern.</summary>
internal static partial class DevSeedLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Dev seed restored the baseline embedding providers (OpenAI/Cloud + Local) — the AddRetrievalEngine migration's own seed rows were missing, which strands every memory and semantic-search path behind 'No default embedding provider is configured'. Expected after a persistence-test run against this shared development database.")]
    public static partial void EmbeddingProvidersReseeded(ILogger logger);
}
