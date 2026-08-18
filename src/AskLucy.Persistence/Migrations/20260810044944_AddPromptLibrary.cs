using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPromptLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PromptAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PromptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DetailsJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("PK_PromptAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PromptCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
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
                    table.PrimaryKey("PK_PromptCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PromptFolders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ParentFolderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Depth = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_PromptFolders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromptFolders_PromptFolders_ParentFolderId",
                        column: x => x.ParentFolderId,
                        principalTable: "PromptFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Prompts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PromptType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SystemInstructions = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DeveloperInstructions = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserInstructions = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContextText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExamplesText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OutputInstructions = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Constraints = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FolderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CurrentVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrentVersionNumber = table.Column<int>(type: "int", nullable: false),
                    IsFavorite = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsPinned = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    RequiresStreaming = table.Column<bool>(type: "bit", nullable: false),
                    RequiresVision = table.Column<bool>(type: "bit", nullable: false),
                    RequiresFunctionCalling = table.Column<bool>(type: "bit", nullable: false),
                    RequiresJsonMode = table.Column<bool>(type: "bit", nullable: false),
                    RequiresReasoning = table.Column<bool>(type: "bit", nullable: false),
                    RequiresEmbeddings = table.Column<bool>(type: "bit", nullable: false),
                    RequiresImageInput = table.Column<bool>(type: "bit", nullable: false),
                    RequiresImageOutput = table.Column<bool>(type: "bit", nullable: false),
                    RequiresAudio = table.Column<bool>(type: "bit", nullable: false),
                    PreferredModelKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
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
                    table.PrimaryKey("PK_Prompts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Prompts_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Prompts_PromptCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "PromptCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Prompts_PromptFolders_FolderId",
                        column: x => x.FolderId,
                        principalTable: "PromptFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PromptTags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PromptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
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
                    table.PrimaryKey("PK_PromptTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromptTags_Prompts_PromptId",
                        column: x => x.PromptId,
                        principalTable: "Prompts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PromptTestCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PromptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    VariableValuesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExpectedOutput = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EvaluationCriteria = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ProviderKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ModelKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SourceExecutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_PromptTestCases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromptTestCases_Prompts_PromptId",
                        column: x => x.PromptId,
                        principalTable: "Prompts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PromptUsageStatistics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PromptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SuccessfulExecutionCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    LastSuccessfulUseAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
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
                    table.PrimaryKey("PK_PromptUsageStatistics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromptUsageStatistics_Prompts_PromptId",
                        column: x => x.PromptId,
                        principalTable: "Prompts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PromptVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PromptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    SystemInstructions = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DeveloperInstructions = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserInstructions = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContextText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExamplesText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OutputInstructions = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Constraints = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProviderKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModelKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Temperature = table.Column<decimal>(type: "decimal(3,2)", nullable: true),
                    MaxOutputTokens = table.Column<int>(type: "int", nullable: true),
                    StructuredOutputRequested = table.Column<bool>(type: "bit", nullable: false),
                    ChangeDescription = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_PromptVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromptVersions_Prompts_PromptId",
                        column: x => x.PromptId,
                        principalTable: "Prompts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PromptExecutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PromptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PromptVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Origin = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ModelKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Temperature = table.Column<decimal>(type: "decimal(3,2)", nullable: true),
                    MaxOutputTokens = table.Column<int>(type: "int", nullable: true),
                    StructuredOutputRequested = table.Column<bool>(type: "bit", nullable: false),
                    ResolvedVariableValuesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RequestedRagContext = table.Column<bool>(type: "bit", nullable: false),
                    RequestedMemoryContext = table.Column<bool>(type: "bit", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ErrorDetail = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    LatencyMs = table.Column<int>(type: "int", nullable: true),
                    ResultMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_PromptExecutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromptExecutions_PromptVersions_PromptVersionId",
                        column: x => x.PromptVersionId,
                        principalTable: "PromptVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PromptExecutions_Prompts_PromptId",
                        column: x => x.PromptId,
                        principalTable: "Prompts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PromptVariables",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PromptVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    VariableType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    DefaultValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExampleValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ValidationRulesJson = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    OrderIndex = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_PromptVariables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromptVariables_PromptVersions_PromptVersionId",
                        column: x => x.PromptVersionId,
                        principalTable: "PromptVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PromptExecutionResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PromptExecutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OutputText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InputTokenCount = table.Column<int>(type: "int", nullable: true),
                    OutputTokenCount = table.Column<int>(type: "int", nullable: true),
                    EstimatedCostUsd = table.Column<decimal>(type: "decimal(18,6)", nullable: true),
                    RagCitationsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MemoryReferencesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
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
                    table.PrimaryKey("PK_PromptExecutionResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromptExecutionResults_PromptExecutions_PromptExecutionId",
                        column: x => x.PromptExecutionId,
                        principalTable: "PromptExecutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PromptRatings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PromptExecutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RatingValue = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RatedByActor = table.Column<string>(type: "nvarchar(max)", nullable: false),
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
                    table.PrimaryKey("PK_PromptRatings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromptRatings_PromptExecutions_PromptExecutionId",
                        column: x => x.PromptExecutionId,
                        principalTable: "PromptExecutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Predefined (OwnerId = null), platform-shared PromptCategory rows, matching
            // spec.md's Prompt Types list — same hand-written InsertData approach
            // KnowledgeBaseCategory's predefined rows use (verified in
            // 20260804044614_AddKnowledgeBaseManagement.cs; tasks.md T025/E1), not HasData().
            var seededAtUtc = new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc);

            migrationBuilder.InsertData(
                table: "PromptCategories",
                columns: new[] { "Id", "OwnerId", "Name", "CreatedAtUtc", "CreatedBy" },
                values: new object[,]
                {
                    { new Guid("d0000000-0000-0000-0000-000000000001"), null, "Chat", seededAtUtc, "system:seed" },
                    { new Guid("d0000000-0000-0000-0000-000000000002"), null, "System", seededAtUtc, "system:seed" },
                    { new Guid("d0000000-0000-0000-0000-000000000003"), null, "Instruction", seededAtUtc, "system:seed" },
                    { new Guid("d0000000-0000-0000-0000-000000000004"), null, "Summarization", seededAtUtc, "system:seed" },
                    { new Guid("d0000000-0000-0000-0000-000000000005"), null, "Translation", seededAtUtc, "system:seed" },
                    { new Guid("d0000000-0000-0000-0000-000000000006"), null, "Extraction", seededAtUtc, "system:seed" },
                    { new Guid("d0000000-0000-0000-0000-000000000007"), null, "Classification", seededAtUtc, "system:seed" },
                    { new Guid("d0000000-0000-0000-0000-000000000008"), null, "RAG", seededAtUtc, "system:seed" },
                    { new Guid("d0000000-0000-0000-0000-000000000009"), null, "Structured Output", seededAtUtc, "system:seed" },
                });

            migrationBuilder.CreateIndex(
                name: "IX_PromptAuditLogs_PromptId",
                table: "PromptAuditLogs",
                column: "PromptId");

            migrationBuilder.CreateIndex(
                name: "IX_PromptCategories_OwnerId",
                table: "PromptCategories",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_PromptExecutionResults_PromptExecutionId",
                table: "PromptExecutionResults",
                column: "PromptExecutionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PromptExecutions_PromptId_CreatedAtUtc",
                table: "PromptExecutions",
                columns: new[] { "PromptId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PromptExecutions_PromptVersionId",
                table: "PromptExecutions",
                column: "PromptVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_PromptExecutions_ResultMessageId",
                table: "PromptExecutions",
                column: "ResultMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_PromptFolders_OwnerId",
                table: "PromptFolders",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_PromptFolders_ParentFolderId",
                table: "PromptFolders",
                column: "ParentFolderId");

            migrationBuilder.CreateIndex(
                name: "IX_PromptRatings_PromptExecutionId",
                table: "PromptRatings",
                column: "PromptExecutionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Prompts_CategoryId",
                table: "Prompts",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Prompts_FolderId",
                table: "Prompts",
                column: "FolderId");

            migrationBuilder.CreateIndex(
                name: "IX_Prompts_IsFavorite",
                table: "Prompts",
                column: "IsFavorite");

            migrationBuilder.CreateIndex(
                name: "IX_Prompts_IsPinned",
                table: "Prompts",
                column: "IsPinned");

            migrationBuilder.CreateIndex(
                name: "IX_Prompts_OwnerId_Name",
                table: "Prompts",
                columns: new[] { "OwnerId", "Name" },
                unique: true,
                filter: "[DeletedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Prompts_Status",
                table: "Prompts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PromptTags_OwnerId_Value",
                table: "PromptTags",
                columns: new[] { "OwnerId", "Value" });

            migrationBuilder.CreateIndex(
                name: "IX_PromptTags_PromptId",
                table: "PromptTags",
                column: "PromptId");

            migrationBuilder.CreateIndex(
                name: "IX_PromptTestCases_PromptId",
                table: "PromptTestCases",
                column: "PromptId");

            migrationBuilder.CreateIndex(
                name: "IX_PromptTestCases_SourceExecutionId",
                table: "PromptTestCases",
                column: "SourceExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_PromptUsageStatistics_LastSuccessfulUseAtUtc",
                table: "PromptUsageStatistics",
                column: "LastSuccessfulUseAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PromptUsageStatistics_PromptId",
                table: "PromptUsageStatistics",
                column: "PromptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PromptVariables_PromptVersionId_Name",
                table: "PromptVariables",
                columns: new[] { "PromptVersionId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PromptVersions_PromptId_VersionNumber",
                table: "PromptVersions",
                columns: new[] { "PromptId", "VersionNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PromptAuditLogs");

            migrationBuilder.DropTable(
                name: "PromptExecutionResults");

            migrationBuilder.DropTable(
                name: "PromptRatings");

            migrationBuilder.DropTable(
                name: "PromptTags");

            migrationBuilder.DropTable(
                name: "PromptTestCases");

            migrationBuilder.DropTable(
                name: "PromptUsageStatistics");

            migrationBuilder.DropTable(
                name: "PromptVariables");

            migrationBuilder.DropTable(
                name: "PromptExecutions");

            migrationBuilder.DropTable(
                name: "PromptVersions");

            migrationBuilder.DropTable(
                name: "Prompts");

            migrationBuilder.DeleteData(table: "PromptCategories", keyColumn: "Id", keyValue: new Guid("d0000000-0000-0000-0000-000000000001"));
            migrationBuilder.DeleteData(table: "PromptCategories", keyColumn: "Id", keyValue: new Guid("d0000000-0000-0000-0000-000000000002"));
            migrationBuilder.DeleteData(table: "PromptCategories", keyColumn: "Id", keyValue: new Guid("d0000000-0000-0000-0000-000000000003"));
            migrationBuilder.DeleteData(table: "PromptCategories", keyColumn: "Id", keyValue: new Guid("d0000000-0000-0000-0000-000000000004"));
            migrationBuilder.DeleteData(table: "PromptCategories", keyColumn: "Id", keyValue: new Guid("d0000000-0000-0000-0000-000000000005"));
            migrationBuilder.DeleteData(table: "PromptCategories", keyColumn: "Id", keyValue: new Guid("d0000000-0000-0000-0000-000000000006"));
            migrationBuilder.DeleteData(table: "PromptCategories", keyColumn: "Id", keyValue: new Guid("d0000000-0000-0000-0000-000000000007"));
            migrationBuilder.DeleteData(table: "PromptCategories", keyColumn: "Id", keyValue: new Guid("d0000000-0000-0000-0000-000000000008"));
            migrationBuilder.DeleteData(table: "PromptCategories", keyColumn: "Id", keyValue: new Guid("d0000000-0000-0000-0000-000000000009"));

            migrationBuilder.DropTable(
                name: "PromptCategories");

            migrationBuilder.DropTable(
                name: "PromptFolders");
        }
    }
}
