using System.Reflection;
using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Authentication;
using AskLucy.Domain.Chats;
using AskLucy.Domain.Consent;
using AskLucy.Domain.Documents;
using AskLucy.Domain.KnowledgeBases;
using AskLucy.Domain.Projects;
using AskLucy.Domain.Prompts;
using AskLucy.Domain.Retrieval;
using AskLucy.Persistence.Identity;
using AskLucy.Persistence.Memory;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MemoryEntities = AskLucy.Domain.Memory;

namespace AskLucy.Persistence;

/// <summary>
/// Migrated from the legacy <c>ChatGPT_ClientContext</c>. Same physical database
/// (connection string key <c>DefaultConnection</c>) so existing production data
/// is migrated in place, per spec.md FR-014/SC-009.
/// </summary>
public sealed class AskLucyDbContext(DbContextOptions<AskLucyDbContext> options, IMemoryContentProtector memoryContentProtector)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<UserChat> UserChats => Set<UserChat>();

    public DbSet<Message> Messages => Set<Message>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<CookieConsentRecord> CookieConsentRecords => Set<CookieConsentRecord>();

    public DbSet<AIProvider> AIProviders => Set<AIProvider>();

    public DbSet<AIModel> AIModels => Set<AIModel>();

    public DbSet<ProviderHealthCheck> ProviderHealthChecks => Set<ProviderHealthCheck>();

    public DbSet<UserAiPreference> UserAiPreferences => Set<UserAiPreference>();

    public DbSet<UserVoicePreference> UserVoicePreferences => Set<UserVoicePreference>();

    public DbSet<VoiceProviderFailoverEvent> VoiceProviderFailoverEvents => Set<VoiceProviderFailoverEvent>();

    public DbSet<KnowledgeBase> KnowledgeBases => Set<KnowledgeBase>();

    public DbSet<KnowledgeBaseFolder> KnowledgeBaseFolders => Set<KnowledgeBaseFolder>();

    public DbSet<KnowledgeBaseDocument> KnowledgeBaseDocuments => Set<KnowledgeBaseDocument>();

    public DbSet<KnowledgeBaseTag> KnowledgeBaseTags => Set<KnowledgeBaseTag>();

    public DbSet<KnowledgeBaseCategory> KnowledgeBaseCategories => Set<KnowledgeBaseCategory>();

    public DbSet<KnowledgeBaseAuditLog> KnowledgeBaseAuditLogs => Set<KnowledgeBaseAuditLog>();

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();

    public DbSet<DocumentFolder> DocumentFolders => Set<DocumentFolder>();

    public DbSet<DocumentMetadata> DocumentMetadata => Set<DocumentMetadata>();

    public DbSet<DocumentLanguage> DocumentLanguages => Set<DocumentLanguage>();

    public DbSet<DocumentCategory> DocumentCategories => Set<DocumentCategory>();

    public DbSet<DocumentClassification> DocumentClassifications => Set<DocumentClassification>();

    public DbSet<DocumentPreview> DocumentPreviews => Set<DocumentPreview>();

    public DbSet<DocumentProcessingJob> DocumentProcessingJobs => Set<DocumentProcessingJob>();

    public DbSet<DocumentProcessingStage> DocumentProcessingStages => Set<DocumentProcessingStage>();

    public DbSet<DocumentProcessingLog> DocumentProcessingLogs => Set<DocumentProcessingLog>();

    public DbSet<DocumentTag> DocumentTags => Set<DocumentTag>();

    public DbSet<DocumentAuditLog> DocumentAuditLogs => Set<DocumentAuditLog>();

    public DbSet<DocumentChecksum> DocumentChecksums => Set<DocumentChecksum>();

    public DbSet<DocumentStatistics> DocumentStatistics => Set<DocumentStatistics>();

    public DbSet<DocumentNotification> DocumentNotifications => Set<DocumentNotification>();

    public DbSet<DocumentUploadSession> DocumentUploadSessions => Set<DocumentUploadSession>();

    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();

    public DbSet<Embedding> Embeddings => Set<Embedding>();

    public DbSet<EmbeddingProvider> EmbeddingProviders => Set<EmbeddingProvider>();

    public DbSet<IndexingJob> IndexingJobs => Set<IndexingJob>();

    public DbSet<IndexingLog> IndexingLogs => Set<IndexingLog>();

    public DbSet<RetrievalHistory> RetrievalHistories => Set<RetrievalHistory>();

    public DbSet<RetrievalResult> RetrievalResults => Set<RetrievalResult>();

    public DbSet<SearchHistory> SearchHistories => Set<SearchHistory>();

    public DbSet<ChunkStatistics> ChunkStatistics => Set<ChunkStatistics>();

    public DbSet<SearchAnalytics> SearchAnalytics => Set<SearchAnalytics>();

    public DbSet<ConversationKnowledgeBase> ConversationKnowledgeBases => Set<ConversationKnowledgeBase>();

    // AI Memory System (specs/018-ai-memory-system) — Foundational.
    public DbSet<MemoryEntities.Memory> Memories => Set<MemoryEntities.Memory>();

    public DbSet<MemoryEntities.MemoryVersion> MemoryVersions => Set<MemoryEntities.MemoryVersion>();

    public DbSet<MemoryEntities.MemoryApproval> MemoryApprovals => Set<MemoryEntities.MemoryApproval>();

    public DbSet<MemoryEntities.MemoryConflict> MemoryConflicts => Set<MemoryEntities.MemoryConflict>();

    public DbSet<MemoryEntities.MemoryEmbedding> MemoryEmbeddings => Set<MemoryEntities.MemoryEmbedding>();

    public DbSet<MemoryEntities.MemoryAuditLog> MemoryAuditLogs => Set<MemoryEntities.MemoryAuditLog>();

    public DbSet<MemoryEntities.MemoryNotification> MemoryNotifications => Set<MemoryEntities.MemoryNotification>();

    public DbSet<MemoryEntities.MemoryPreference> MemoryPreferences => Set<MemoryEntities.MemoryPreference>();

    public DbSet<MemoryEntities.MemoryCategoryPreference> MemoryCategoryPreferences => Set<MemoryEntities.MemoryCategoryPreference>();

    public DbSet<MemoryEntities.MemoryReference> MemoryReferences => Set<MemoryEntities.MemoryReference>();

    public DbSet<MemoryEntities.MemoryExportJob> MemoryExportJobs => Set<MemoryEntities.MemoryExportJob>();

    public DbSet<Project> Projects => Set<Project>();

    // Prompt Library & Prompt Engineering Workspace (specs/019-prompt-library-workspace).
    public DbSet<Prompt> Prompts => Set<Prompt>();

    public DbSet<PromptVersion> PromptVersions => Set<PromptVersion>();

    public DbSet<PromptVariable> PromptVariables => Set<PromptVariable>();

    public DbSet<PromptCategory> PromptCategories => Set<PromptCategory>();

    public DbSet<PromptTag> PromptTags => Set<PromptTag>();

    public DbSet<PromptFolder> PromptFolders => Set<PromptFolder>();

    public DbSet<PromptTestCase> PromptTestCases => Set<PromptTestCase>();

    public DbSet<PromptExecution> PromptExecutions => Set<PromptExecution>();

    public DbSet<PromptExecutionResult> PromptExecutionResults => Set<PromptExecutionResult>();

    public DbSet<PromptRating> PromptRatings => Set<PromptRating>();

    public DbSet<PromptUsageStatistics> PromptUsageStatistics => Set<PromptUsageStatistics>();

    public DbSet<PromptAuditLog> PromptAuditLogs => Set<PromptAuditLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // AI Memory System (specs/018-ai-memory-system, research.md Decision 12) — content columns
        // encrypted at rest via the existing IAiCredentialProtector mechanism. Applied here, not in
        // each entity's IEntityTypeConfiguration, because the converter needs this DbContext's
        // DI-injected protector instance.
        var contentConverter = new EncryptedStringConverter(memoryContentProtector);
        builder.Entity<MemoryEntities.Memory>().Property(m => m.Content).HasConversion(contentConverter);
        builder.Entity<MemoryEntities.MemoryVersion>().Property(v => v.PreviousContent).HasConversion(contentConverter);
        builder.Entity<MemoryEntities.MemoryReference>().Property(r => r.ContentSnapshot).HasConversion(contentConverter);

        // Deliberately no HasData() seeding here: unlike the legacy ChatGPT_ClientContext,
        // this migration does not re-author a hardcoded seed-admin credential in new code.
        // Existing production users/roles are carried over via the in-place migration
        // (research.md Topic 5), not re-seeded — see spec.md's Assumptions/Risks.
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        configurationBuilder.Properties<DateOnly>()
            .HaveConversion<DateOnlyConverter>()
            .HaveColumnType("date");

        configurationBuilder.Properties<DateOnly?>()
            .HaveConversion<NullableDateOnlyConverter>()
            .HaveColumnType("date");
    }
}

/// <summary>Converts <see cref="DateOnly"/> to <see cref="DateTime"/> and back (unchanged from the legacy context).</summary>
public sealed class DateOnlyConverter() : ValueConverter<DateOnly, DateTime>(
    d => d.ToDateTime(TimeOnly.MinValue),
    d => DateOnly.FromDateTime(d));

public sealed class NullableDateOnlyConverter() : ValueConverter<DateOnly?, DateTime?>(
    d => d == null ? null : new DateTime?(d.Value.ToDateTime(TimeOnly.MinValue)),
    d => d == null ? null : new DateOnly?(DateOnly.FromDateTime(d.Value)));
