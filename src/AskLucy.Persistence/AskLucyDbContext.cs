using System.Reflection;
using AskLucy.Application.Abstractions;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Authentication;
using AskLucy.Domain.Chats;
using AskLucy.Domain.Consent;
using AskLucy.Domain.Documents;
using AskLucy.Domain.KnowledgeBases;
using AskLucy.Domain.Mcp;
using AskLucy.Domain.Panels;
using AskLucy.Domain.Projects;
using AskLucy.Domain.Prompts;
using AskLucy.Domain.Retrieval;
using AskLucy.Domain.Workflows;
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

    public DbSet<UserPanelPreference> UserPanelPreferences => Set<UserPanelPreference>();

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

    // AI Agent Framework & Agent Runtime (specs/020-ai-agent-framework).
    public DbSet<Agent> Agents => Set<Agent>();

    public DbSet<AgentVersion> AgentVersions => Set<AgentVersion>();

    public DbSet<AgentTool> AgentTools => Set<AgentTool>();

    public DbSet<AgentKnowledgeBase> AgentKnowledgeBases => Set<AgentKnowledgeBase>();

    public DbSet<AgentMemoryPolicy> AgentMemoryPolicies => Set<AgentMemoryPolicy>();

    public DbSet<AgentExecution> AgentExecutions => Set<AgentExecution>();

    public DbSet<AgentExecutionStep> AgentExecutionSteps => Set<AgentExecutionStep>();

    public DbSet<AgentExecutionEvent> AgentExecutionEvents => Set<AgentExecutionEvent>();

    public DbSet<AgentToolCall> AgentToolCalls => Set<AgentToolCall>();

    public DbSet<AgentApproval> AgentApprovals => Set<AgentApproval>();

    public DbSet<AgentExecutionError> AgentExecutionErrors => Set<AgentExecutionError>();

    public DbSet<AgentExecutionUsage> AgentExecutionUsages => Set<AgentExecutionUsage>();

    public DbSet<AgentExecutionCost> AgentExecutionCosts => Set<AgentExecutionCost>();

    public DbSet<AgentPolicy> AgentPolicies => Set<AgentPolicy>();

    public DbSet<AgentUserExecutionLimit> AgentUserExecutionLimits => Set<AgentUserExecutionLimit>();

    public DbSet<AgentAuditLog> AgentAuditLogs => Set<AgentAuditLog>();

    public DbSet<McpServer> McpServers => Set<McpServer>();

    public DbSet<McpServerCredential> McpServerCredentials => Set<McpServerCredential>();

    public DbSet<McpServerHealth> McpServerHealths => Set<McpServerHealth>();

    public DbSet<McpCapabilitySnapshot> McpCapabilitySnapshots => Set<McpCapabilitySnapshot>();

    public DbSet<McpTool> McpTools => Set<McpTool>();

    public DbSet<McpResource> McpResources => Set<McpResource>();

    public DbSet<McpPrompt> McpPrompts => Set<McpPrompt>();

    public DbSet<McpAuditLog> McpAuditLogs => Set<McpAuditLog>();

    public DbSet<Workflow> Workflows => Set<Workflow>();

    public DbSet<WorkflowVersion> WorkflowVersions => Set<WorkflowVersion>();

    public DbSet<WorkflowNode> WorkflowNodes => Set<WorkflowNode>();

    public DbSet<WorkflowConnection> WorkflowConnections => Set<WorkflowConnection>();

    public DbSet<WorkflowVariable> WorkflowVariables => Set<WorkflowVariable>();

    public DbSet<WorkflowExecution> WorkflowExecutions => Set<WorkflowExecution>();

    public DbSet<WorkflowExecutionNode> WorkflowExecutionNodes => Set<WorkflowExecutionNode>();

    public DbSet<WorkflowExecutionEvent> WorkflowExecutionEvents => Set<WorkflowExecutionEvent>();

    public DbSet<WorkflowApproval> WorkflowApprovals => Set<WorkflowApproval>();

    public DbSet<WorkflowError> WorkflowErrors => Set<WorkflowError>();

    public DbSet<WorkflowExecutionUsage> WorkflowExecutionUsages => Set<WorkflowExecutionUsage>();

    public DbSet<WorkflowExecutionCost> WorkflowExecutionCosts => Set<WorkflowExecutionCost>();

    public DbSet<WorkflowPolicy> WorkflowPolicies => Set<WorkflowPolicy>();

    public DbSet<WorkflowUserExecutionLimit> WorkflowUserExecutionLimits => Set<WorkflowUserExecutionLimit>();

    public DbSet<WorkflowAuditLog> WorkflowAuditLogs => Set<WorkflowAuditLog>();

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

    // EF Core doesn't guarantee an existing, concurrency-checked parent's UPDATE executes
    // before an unrelated new child's INSERT within one SaveChanges batch (no generated-key
    // dependency forces that order) — so Prompt.ApplyEdit's PromptVersions insert can race its
    // own Prompts row update, and a stale-read edit conflict surfaces as a raw unique-index-
    // violation DbUpdateException instead of DbUpdateConcurrencyException.
    // IX_PromptVersions_PromptId_VersionNumber can *only* be violated this way (VersionNumber is
    // always CurrentVersionNumber+1 from an in-memory read), so re-throwing as
    // DbUpdateConcurrencyException here is a correct translation, not a guess.
    // Overridden here (not in UnitOfWork, Application's transaction-boundary abstraction) so it
    // applies to every caller of this context's SaveChangesAsync, not only ones that go through
    // IUnitOfWork — ProblemDetailsMiddleware already maps DbUpdateConcurrencyException to a 409.
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsPromptVersionNumberConflict(ex))
        {
            throw new DbUpdateConcurrencyException(
                "The prompt was modified by another request before this edit's new version could be saved.", ex);
        }
    }

    private static bool IsPromptVersionNumberConflict(DbUpdateException ex) =>
        ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 } sqlException &&
        sqlException.Message.Contains("IX_PromptVersions_PromptId_VersionNumber", StringComparison.Ordinal);

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
