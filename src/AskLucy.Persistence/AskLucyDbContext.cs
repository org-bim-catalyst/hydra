using System.Reflection;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Authentication;
using AskLucy.Domain.Chats;
using AskLucy.Domain.Consent;
using AskLucy.Domain.Documents;
using AskLucy.Domain.KnowledgeBases;
using AskLucy.Persistence.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AskLucy.Persistence;

/// <summary>
/// Migrated from the legacy <c>ChatGPT_ClientContext</c>. Same physical database
/// (connection string key <c>DefaultConnection</c>) so existing production data
/// is migrated in place, per spec.md FR-014/SC-009.
/// </summary>
public sealed class AskLucyDbContext(DbContextOptions<AskLucyDbContext> options)
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

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

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
