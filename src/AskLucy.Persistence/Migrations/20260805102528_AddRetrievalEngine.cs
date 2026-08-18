using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRetrievalEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RetrievalMaxContextTokens",
                table: "UserChats",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RetrievalSearchMode",
                table: "UserChats",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RetrievalSimilarityThreshold",
                table: "UserChats",
                type: "decimal(5,4)",
                precision: 5,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RetrievalTopK",
                table: "UserChats",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChunkingStrategy",
                table: "KnowledgeBases",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "EmbeddingProviderId",
                table: "KnowledgeBases",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IndexStatus",
                table: "KnowledgeBases",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastIndexedAtUtc",
                table: "KnowledgeBases",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresDataResidency",
                table: "KnowledgeBases",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "DocumentId",
                table: "KnowledgeBaseDocuments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DocumentChunkId",
                table: "Citations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DocumentId",
                table: "Citations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DocumentVersionId",
                table: "Citations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "KnowledgeBaseId",
                table: "Citations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PageNumber",
                table: "Citations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Section",
                table: "Citations",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChunkStatistics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KnowledgeBaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TotalChunks = table.Column<int>(type: "int", nullable: false),
                    TotalEmbeddings = table.Column<int>(type: "int", nullable: false),
                    StorageBytes = table.Column<long>(type: "bigint", nullable: false),
                    ComputedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChunkStatistics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChunkStatistics_KnowledgeBases_KnowledgeBaseId",
                        column: x => x.KnowledgeBaseId,
                        principalTable: "KnowledgeBases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConversationKnowledgeBases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserChatId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KnowledgeBaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttachedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationKnowledgeBases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConversationKnowledgeBases_KnowledgeBases_KnowledgeBaseId",
                        column: x => x.KnowledgeBaseId,
                        principalTable: "KnowledgeBases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConversationKnowledgeBases_UserChats_UserChatId",
                        column: x => x.UserChatId,
                        principalTable: "UserChats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocumentChunks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KnowledgeBaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KnowledgeBaseDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChunkingStrategy = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TokenCount = table.Column<int>(type: "int", nullable: false),
                    CharacterCount = table.Column<int>(type: "int", nullable: false),
                    Language = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    PageNumber = table.Column<int>(type: "int", nullable: true),
                    Section = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Heading = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Position = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentChunks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentChunks_DocumentVersions_DocumentVersionId",
                        column: x => x.DocumentVersionId,
                        principalTable: "DocumentVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentChunks_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentChunks_KnowledgeBaseDocuments_KnowledgeBaseDocumentId",
                        column: x => x.KnowledgeBaseDocumentId,
                        principalTable: "KnowledgeBaseDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentChunks_KnowledgeBases_KnowledgeBaseId",
                        column: x => x.KnowledgeBaseId,
                        principalTable: "KnowledgeBases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Keyword/hybrid search (research.md Decision 6) — a full-text catalog/index on
            // DocumentChunks.Content, created only where Full-Text Search is actually installed
            // (SERVERPROPERTY('IsFullTextInstalled')). Confirmed during /speckit-implement: SQL
            // Server LocalDB ("user instance" mode) reports this as 0 and rejects full-text DDL
            // outright ("Cannot use full-text search in user instance") — the EXEC(...) dynamic
            // SQL defers parsing so the IF branch is safely skippable on LocalDB without a parse
            // error, while still creating the real index on any full SQL Server edition with the
            // feature installed (expected for staging/production).
            migrationBuilder.Sql(
                """
                IF (SELECT CAST(SERVERPROPERTY('IsFullTextInstalled') AS int)) = 1
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = 'RetrievalFullTextCatalog')
                        EXEC('CREATE FULLTEXT CATALOG RetrievalFullTextCatalog');
                    EXEC('CREATE FULLTEXT INDEX ON [DocumentChunks]([Content]) KEY INDEX [PK_DocumentChunks] ON RetrievalFullTextCatalog WITH CHANGE_TRACKING AUTO');
                END
                """);

            migrationBuilder.CreateTable(
                name: "EmbeddingProviders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Vendor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ModelKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Dimensionality = table.Column<int>(type: "int", nullable: false),
                    HostingType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmbeddingProviders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IndexingJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KnowledgeBaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KnowledgeBaseDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    JobType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RetryCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    MaxRetries = table.Column<int>(type: "int", nullable: false),
                    HangfireJobId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndexingJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IndexingJobs_KnowledgeBaseDocuments_KnowledgeBaseDocumentId",
                        column: x => x.KnowledgeBaseDocumentId,
                        principalTable: "KnowledgeBaseDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IndexingJobs_KnowledgeBases_KnowledgeBaseId",
                        column: x => x.KnowledgeBaseId,
                        principalTable: "KnowledgeBases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RetrievalHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserChatId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Query = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    SearchMode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    KnowledgeBaseIdsSearchedJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TopK = table.Column<int>(type: "int", nullable: false),
                    SimilarityThreshold = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    MaxContextTokens = table.Column<int>(type: "int", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DurationMs = table.Column<int>(type: "int", nullable: false),
                    ResultCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetrievalHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RetrievalHistories_Messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "Messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RetrievalHistories_UserChats_UserChatId",
                        column: x => x.UserChatId,
                        principalTable: "UserChats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SearchAnalytics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KnowledgeBaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SearchCount = table.Column<int>(type: "int", nullable: false),
                    AverageRetrievalTimeMs = table.Column<int>(type: "int", nullable: true),
                    AverageSimilarityScore = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: true),
                    FailedSearchCount = table.Column<int>(type: "int", nullable: false),
                    EmptySearchCount = table.Column<int>(type: "int", nullable: false),
                    TopDocumentsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ComputedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchAnalytics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SearchAnalytics_KnowledgeBases_KnowledgeBaseId",
                        column: x => x.KnowledgeBaseId,
                        principalTable: "KnowledgeBases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SearchHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Query = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    SearchMode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    KnowledgeBaseIdsSearchedJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FiltersJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResultCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchHistories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Embeddings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentChunkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmbeddingProviderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Embeddings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Embeddings_DocumentChunks_DocumentChunkId",
                        column: x => x.DocumentChunkId,
                        principalTable: "DocumentChunks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Embeddings_EmbeddingProviders_EmbeddingProviderId",
                        column: x => x.EmbeddingProviderId,
                        principalTable: "EmbeddingProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Embeddings.Vector — added via raw SQL rather than the generated CreateTable columns
            // above (research.md Decision 3; see EmbeddingConfiguration's remarks for why EF's
            // Fluent API can't reach the native vector mapping in this package version). NULLable:
            // EF's own INSERT for a new Embedding row (via IEmbeddingRepository) never populates
            // this column (it's outside the EF model), so IVectorStore.UpsertAsync fills it in
            // via a separate raw UPDATE immediately afterward — a two-step write, not a single
            // atomic insert. Confirmed against a real local SQL Server 2025 (RTM-CU3) instance
            // during /speckit-implement: `VECTOR(n)`, `CAST(json AS VECTOR(n))`, and
            // `VECTOR_DISTANCE` all work correctly. No vector index is created on this column
            // deliberately, not as a deferred gap: verified directly against the real hosted
            // Test database that CREATE VECTOR INDEX produces the pre-Azure/Fabric index format
            // on this non-Azure SQL Server 2025 edition (sys.vector_indexes.index_version = NULL,
            // not the latest "3" format), which makes the table permanently read-only for all DML,
            // incompatible with this engine's continuous incremental-indexing requirement
            // (FR-010/FR-011/US5). See research.md Decision 3 (Vector index - deliberately not
            // used) for the full finding. Searches scan this column directly via VECTOR_DISTANCE.
            migrationBuilder.Sql(
                $"ALTER TABLE [Embeddings] ADD [Vector] VECTOR({AskLucy.Domain.Retrieval.Embedding.VectorWidth}) NULL;");

            migrationBuilder.CreateTable(
                name: "IndexingLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IndexingJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Stage = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndexingLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IndexingLogs_IndexingJobs_IndexingJobId",
                        column: x => x.IndexingJobId,
                        principalTable: "IndexingJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RetrievalResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RetrievalHistoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentChunkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    RelevanceScore = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    SemanticScore = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    KeywordScore = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    BoostFactorsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetrievalResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RetrievalResults_DocumentChunks_DocumentChunkId",
                        column: x => x.DocumentChunkId,
                        principalTable: "DocumentChunks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RetrievalResults_RetrievalHistories_RetrievalHistoryId",
                        column: x => x.RetrievalHistoryId,
                        principalTable: "RetrievalHistories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBases_EmbeddingProviderId",
                table: "KnowledgeBases",
                column: "EmbeddingProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBases_IndexStatus",
                table: "KnowledgeBases",
                column: "IndexStatus");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBaseDocuments_DocumentId",
                table: "KnowledgeBaseDocuments",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Citations_DocumentChunkId",
                table: "Citations",
                column: "DocumentChunkId");

            migrationBuilder.CreateIndex(
                name: "IX_ChunkStatistics_KnowledgeBaseId",
                table: "ChunkStatistics",
                column: "KnowledgeBaseId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationKnowledgeBases_KnowledgeBaseId",
                table: "ConversationKnowledgeBases",
                column: "KnowledgeBaseId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationKnowledgeBases_UserChatId_KnowledgeBaseId",
                table: "ConversationKnowledgeBases",
                columns: new[] { "UserChatId", "KnowledgeBaseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_ContentHash",
                table: "DocumentChunks",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_DocumentId",
                table: "DocumentChunks",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_DocumentVersionId",
                table: "DocumentChunks",
                column: "DocumentVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_DocumentVersionId_Position",
                table: "DocumentChunks",
                columns: new[] { "DocumentVersionId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_KnowledgeBaseDocumentId",
                table: "DocumentChunks",
                column: "KnowledgeBaseDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_KnowledgeBaseId",
                table: "DocumentChunks",
                column: "KnowledgeBaseId");

            migrationBuilder.CreateIndex(
                name: "IX_EmbeddingProviders_HostingType",
                table: "EmbeddingProviders",
                column: "HostingType");

            migrationBuilder.CreateIndex(
                name: "IX_EmbeddingProviders_HostingType_IsDefault",
                table: "EmbeddingProviders",
                columns: new[] { "HostingType", "IsDefault" });

            migrationBuilder.CreateIndex(
                name: "IX_Embeddings_DocumentChunkId",
                table: "Embeddings",
                column: "DocumentChunkId");

            migrationBuilder.CreateIndex(
                name: "IX_Embeddings_DocumentChunkId_IsCurrent",
                table: "Embeddings",
                columns: new[] { "DocumentChunkId", "IsCurrent" });

            migrationBuilder.CreateIndex(
                name: "IX_Embeddings_EmbeddingProviderId",
                table: "Embeddings",
                column: "EmbeddingProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_IndexingJobs_KnowledgeBaseDocumentId",
                table: "IndexingJobs",
                column: "KnowledgeBaseDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_IndexingJobs_KnowledgeBaseId",
                table: "IndexingJobs",
                column: "KnowledgeBaseId");

            migrationBuilder.CreateIndex(
                name: "IX_IndexingJobs_KnowledgeBaseId_Status",
                table: "IndexingJobs",
                columns: new[] { "KnowledgeBaseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_IndexingLogs_IndexingJobId_OccurredAtUtc",
                table: "IndexingLogs",
                columns: new[] { "IndexingJobId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RetrievalHistories_CreatedAtUtc",
                table: "RetrievalHistories",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RetrievalHistories_MessageId",
                table: "RetrievalHistories",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_RetrievalHistories_UserChatId",
                table: "RetrievalHistories",
                column: "UserChatId");

            migrationBuilder.CreateIndex(
                name: "IX_RetrievalHistories_UserId",
                table: "RetrievalHistories",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_RetrievalResults_DocumentChunkId",
                table: "RetrievalResults",
                column: "DocumentChunkId");

            migrationBuilder.CreateIndex(
                name: "IX_RetrievalResults_RetrievalHistoryId",
                table: "RetrievalResults",
                column: "RetrievalHistoryId");

            migrationBuilder.CreateIndex(
                name: "IX_SearchAnalytics_KnowledgeBaseId",
                table: "SearchAnalytics",
                column: "KnowledgeBaseId");

            migrationBuilder.CreateIndex(
                name: "IX_SearchAnalytics_UserId_KnowledgeBaseId",
                table: "SearchAnalytics",
                columns: new[] { "UserId", "KnowledgeBaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_SearchHistories_CreatedAtUtc",
                table: "SearchHistories",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SearchHistories_UserId",
                table: "SearchHistories",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_KnowledgeBaseDocuments_Documents_DocumentId",
                table: "KnowledgeBaseDocuments",
                column: "DocumentId",
                principalTable: "Documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_KnowledgeBases_EmbeddingProviders_EmbeddingProviderId",
                table: "KnowledgeBases",
                column: "EmbeddingProviderId",
                principalTable: "EmbeddingProviders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Default EmbeddingProvider rows (T028, research.md Decision 5) — one Cloud default
            // (OpenAI) and one Local default (in-process ONNX), so a knowledge base's
            // EmbeddingProviderId can resolve to a real row immediately, and FR-009a's data-
            // residency requirement has a Local option to select from at launch.
            var embeddingProviderSeededAtUtc = new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc);

            migrationBuilder.InsertData(
                table: "EmbeddingProviders",
                columns: new[] { "Id", "Vendor", "ModelKey", "Dimensionality", "HostingType", "IsDefault", "IsActive", "CreatedAtUtc", "CreatedBy" },
                values: new object[,]
                {
                    { new Guid("e0000000-0000-0000-0000-000000000001"), "OpenAI", "text-embedding-3-small", 1536, "Cloud", true, true, embeddingProviderSeededAtUtc, "system:seed" },
                    { new Guid("e0000000-0000-0000-0000-000000000002"), "Local", "onnx-minilm-l6-v2", 384, "Local", true, true, embeddingProviderSeededAtUtc, "system:seed" },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_KnowledgeBaseDocuments_Documents_DocumentId",
                table: "KnowledgeBaseDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_KnowledgeBases_EmbeddingProviders_EmbeddingProviderId",
                table: "KnowledgeBases");

            migrationBuilder.DeleteData(table: "EmbeddingProviders", keyColumn: "Id", keyValue: new Guid("e0000000-0000-0000-0000-000000000001"));
            migrationBuilder.DeleteData(table: "EmbeddingProviders", keyColumn: "Id", keyValue: new Guid("e0000000-0000-0000-0000-000000000002"));

            migrationBuilder.DropTable(
                name: "ChunkStatistics");

            migrationBuilder.DropTable(
                name: "ConversationKnowledgeBases");

            migrationBuilder.DropTable(
                name: "Embeddings");

            migrationBuilder.DropTable(
                name: "IndexingLogs");

            migrationBuilder.DropTable(
                name: "RetrievalResults");

            migrationBuilder.DropTable(
                name: "SearchAnalytics");

            migrationBuilder.DropTable(
                name: "SearchHistories");

            migrationBuilder.DropTable(
                name: "EmbeddingProviders");

            migrationBuilder.DropTable(
                name: "IndexingJobs");

            // Must drop the full-text index (if it was created — see Up()) before the table
            // itself can be dropped.
            migrationBuilder.Sql(
                """
                IF (SELECT CAST(SERVERPROPERTY('IsFullTextInstalled') AS int)) = 1
                BEGIN
                    IF EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('[DocumentChunks]'))
                        EXEC('DROP FULLTEXT INDEX ON [DocumentChunks]');
                    IF EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = 'RetrievalFullTextCatalog')
                        EXEC('DROP FULLTEXT CATALOG RetrievalFullTextCatalog');
                END
                """);

            migrationBuilder.DropTable(
                name: "DocumentChunks");

            migrationBuilder.DropTable(
                name: "RetrievalHistories");

            migrationBuilder.DropIndex(
                name: "IX_KnowledgeBases_EmbeddingProviderId",
                table: "KnowledgeBases");

            migrationBuilder.DropIndex(
                name: "IX_KnowledgeBases_IndexStatus",
                table: "KnowledgeBases");

            migrationBuilder.DropIndex(
                name: "IX_KnowledgeBaseDocuments_DocumentId",
                table: "KnowledgeBaseDocuments");

            migrationBuilder.DropIndex(
                name: "IX_Citations_DocumentChunkId",
                table: "Citations");

            migrationBuilder.DropColumn(
                name: "RetrievalMaxContextTokens",
                table: "UserChats");

            migrationBuilder.DropColumn(
                name: "RetrievalSearchMode",
                table: "UserChats");

            migrationBuilder.DropColumn(
                name: "RetrievalSimilarityThreshold",
                table: "UserChats");

            migrationBuilder.DropColumn(
                name: "RetrievalTopK",
                table: "UserChats");

            migrationBuilder.DropColumn(
                name: "ChunkingStrategy",
                table: "KnowledgeBases");

            migrationBuilder.DropColumn(
                name: "EmbeddingProviderId",
                table: "KnowledgeBases");

            migrationBuilder.DropColumn(
                name: "IndexStatus",
                table: "KnowledgeBases");

            migrationBuilder.DropColumn(
                name: "LastIndexedAtUtc",
                table: "KnowledgeBases");

            migrationBuilder.DropColumn(
                name: "RequiresDataResidency",
                table: "KnowledgeBases");

            migrationBuilder.DropColumn(
                name: "DocumentId",
                table: "KnowledgeBaseDocuments");

            migrationBuilder.DropColumn(
                name: "DocumentChunkId",
                table: "Citations");

            migrationBuilder.DropColumn(
                name: "DocumentId",
                table: "Citations");

            migrationBuilder.DropColumn(
                name: "DocumentVersionId",
                table: "Citations");

            migrationBuilder.DropColumn(
                name: "KnowledgeBaseId",
                table: "Citations");

            migrationBuilder.DropColumn(
                name: "PageNumber",
                table: "Citations");

            migrationBuilder.DropColumn(
                name: "Section",
                table: "Citations");
        }
    }
}
