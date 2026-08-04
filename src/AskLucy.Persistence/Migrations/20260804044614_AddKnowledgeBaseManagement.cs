using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeBaseManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KnowledgeBaseAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KnowledgeBaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_KnowledgeBaseAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeBaseCategories",
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
                    table.PrimaryKey("PK_KnowledgeBaseCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeBases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Visibility = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Color = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: true),
                    Icon = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    IsFavorite = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    PinnedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DocumentCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    TotalPageCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    StorageSizeBytes = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    PurgeScheduledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
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
                    table.PrimaryKey("PK_KnowledgeBases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KnowledgeBases_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KnowledgeBases_KnowledgeBaseCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "KnowledgeBaseCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeBaseFolders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KnowledgeBaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_KnowledgeBaseFolders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KnowledgeBaseFolders_KnowledgeBaseFolders_ParentFolderId",
                        column: x => x.ParentFolderId,
                        principalTable: "KnowledgeBaseFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KnowledgeBaseFolders_KnowledgeBases_KnowledgeBaseId",
                        column: x => x.KnowledgeBaseId,
                        principalTable: "KnowledgeBases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeBaseTags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KnowledgeBaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_KnowledgeBaseTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KnowledgeBaseTags_KnowledgeBases_KnowledgeBaseId",
                        column: x => x.KnowledgeBaseId,
                        principalTable: "KnowledgeBases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeBaseDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KnowledgeBaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FolderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    StoredFileName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    PageCount = table.Column<int>(type: "int", nullable: true),
                    ProcessingStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_KnowledgeBaseDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KnowledgeBaseDocuments_KnowledgeBaseFolders_FolderId",
                        column: x => x.FolderId,
                        principalTable: "KnowledgeBaseFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KnowledgeBaseDocuments_KnowledgeBases_KnowledgeBaseId",
                        column: x => x.KnowledgeBaseId,
                        principalTable: "KnowledgeBases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBaseAuditLogs_KnowledgeBaseId_OccurredAtUtc",
                table: "KnowledgeBaseAuditLogs",
                columns: new[] { "KnowledgeBaseId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBaseCategories_OwnerId",
                table: "KnowledgeBaseCategories",
                column: "OwnerId");

            // Baseline seed (FR-017): the 8 predefined, platform-wide-shared categories.
            // Not a HasData() entry on the Configuration class — this is a one-time seed, not
            // a value the model needs to keep reconciling on every future migration (mirrors
            // AddMultiProviderAiEngine's AIProviders/AIModels seed).
            var seededAtUtc = new DateTime(2026, 8, 4, 0, 0, 0, DateTimeKind.Utc);

            migrationBuilder.InsertData(
                table: "KnowledgeBaseCategories",
                columns: new[] { "Id", "OwnerId", "Name", "CreatedAtUtc", "CreatedBy" },
                values: new object[,]
                {
                    { new Guid("c0000000-0000-0000-0000-000000000001"), null, "Engineering", seededAtUtc, "system:seed" },
                    { new Guid("c0000000-0000-0000-0000-000000000002"), null, "Architecture", seededAtUtc, "system:seed" },
                    { new Guid("c0000000-0000-0000-0000-000000000003"), null, "Construction", seededAtUtc, "system:seed" },
                    { new Guid("c0000000-0000-0000-0000-000000000004"), null, "Legal", seededAtUtc, "system:seed" },
                    { new Guid("c0000000-0000-0000-0000-000000000005"), null, "Finance", seededAtUtc, "system:seed" },
                    { new Guid("c0000000-0000-0000-0000-000000000006"), null, "Research", seededAtUtc, "system:seed" },
                    { new Guid("c0000000-0000-0000-0000-000000000007"), null, "Education", seededAtUtc, "system:seed" },
                    { new Guid("c0000000-0000-0000-0000-000000000008"), null, "General", seededAtUtc, "system:seed" },
                });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBaseDocuments_FolderId",
                table: "KnowledgeBaseDocuments",
                column: "FolderId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBaseDocuments_KnowledgeBaseId",
                table: "KnowledgeBaseDocuments",
                column: "KnowledgeBaseId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBaseFolders_KnowledgeBaseId",
                table: "KnowledgeBaseFolders",
                column: "KnowledgeBaseId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBaseFolders_ParentFolderId",
                table: "KnowledgeBaseFolders",
                column: "ParentFolderId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBases_CategoryId",
                table: "KnowledgeBases",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBases_CreatedAtUtc",
                table: "KnowledgeBases",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBases_IsFavorite",
                table: "KnowledgeBases",
                column: "IsFavorite");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBases_ModifiedAtUtc",
                table: "KnowledgeBases",
                column: "ModifiedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBases_OwnerId",
                table: "KnowledgeBases",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBases_PinnedAtUtc",
                table: "KnowledgeBases",
                column: "PinnedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBases_PurgeScheduledAtUtc",
                table: "KnowledgeBases",
                column: "PurgeScheduledAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBases_Status",
                table: "KnowledgeBases",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBaseTags_KnowledgeBaseId",
                table: "KnowledgeBaseTags",
                column: "KnowledgeBaseId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBaseTags_OwnerId_Value",
                table: "KnowledgeBaseTags",
                columns: new[] { "OwnerId", "Value" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(table: "KnowledgeBaseCategories", keyColumn: "Id", keyValue: new Guid("c0000000-0000-0000-0000-000000000001"));
            migrationBuilder.DeleteData(table: "KnowledgeBaseCategories", keyColumn: "Id", keyValue: new Guid("c0000000-0000-0000-0000-000000000002"));
            migrationBuilder.DeleteData(table: "KnowledgeBaseCategories", keyColumn: "Id", keyValue: new Guid("c0000000-0000-0000-0000-000000000003"));
            migrationBuilder.DeleteData(table: "KnowledgeBaseCategories", keyColumn: "Id", keyValue: new Guid("c0000000-0000-0000-0000-000000000004"));
            migrationBuilder.DeleteData(table: "KnowledgeBaseCategories", keyColumn: "Id", keyValue: new Guid("c0000000-0000-0000-0000-000000000005"));
            migrationBuilder.DeleteData(table: "KnowledgeBaseCategories", keyColumn: "Id", keyValue: new Guid("c0000000-0000-0000-0000-000000000006"));
            migrationBuilder.DeleteData(table: "KnowledgeBaseCategories", keyColumn: "Id", keyValue: new Guid("c0000000-0000-0000-0000-000000000007"));
            migrationBuilder.DeleteData(table: "KnowledgeBaseCategories", keyColumn: "Id", keyValue: new Guid("c0000000-0000-0000-0000-000000000008"));

            migrationBuilder.DropTable(
                name: "KnowledgeBaseAuditLogs");

            migrationBuilder.DropTable(
                name: "KnowledgeBaseDocuments");

            migrationBuilder.DropTable(
                name: "KnowledgeBaseTags");

            migrationBuilder.DropTable(
                name: "KnowledgeBaseFolders");

            migrationBuilder.DropTable(
                name: "KnowledgeBases");

            migrationBuilder.DropTable(
                name: "KnowledgeBaseCategories");
        }
    }
}
