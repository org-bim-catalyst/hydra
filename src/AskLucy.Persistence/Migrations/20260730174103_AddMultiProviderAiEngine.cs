using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiProviderAiEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GenerationParametersJson",
                table: "UserChats",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ModelId",
                table: "UserChats",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProviderId",
                table: "UserChats",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CachedTokenCount",
                table: "Messages",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ComparisonGroupId",
                table: "Messages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedCostUsd",
                table: "Messages",
                type: "decimal(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsIncludedInContext",
                table: "Messages",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "LatencyMs",
                table: "Messages",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReasoningTokenCount",
                table: "Messages",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AIModels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModelKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ContextWindowTokens = table.Column<int>(type: "int", nullable: false),
                    MaxOutputTokens = table.Column<int>(type: "int", nullable: false),
                    SupportsStreaming = table.Column<bool>(type: "bit", nullable: false),
                    SupportsVision = table.Column<bool>(type: "bit", nullable: false),
                    SupportsFunctionCalling = table.Column<bool>(type: "bit", nullable: false),
                    SupportsJsonMode = table.Column<bool>(type: "bit", nullable: false),
                    SupportsReasoning = table.Column<bool>(type: "bit", nullable: false),
                    SupportsEmbeddings = table.Column<bool>(type: "bit", nullable: false),
                    SupportsImageInput = table.Column<bool>(type: "bit", nullable: false),
                    SupportsImageOutput = table.Column<bool>(type: "bit", nullable: false),
                    SupportsAudio = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReleaseDate = table.Column<DateTime>(type: "date", nullable: true),
                    InputPricePerMillionTokensUsd = table.Column<decimal>(type: "decimal(18,6)", nullable: true),
                    OutputPricePerMillionTokensUsd = table.Column<decimal>(type: "decimal(18,6)", nullable: true),
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
                    table.PrimaryKey("PK_AIModels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AIProviders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CredentialCiphertext = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CredentialLastRotatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DefaultModelId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    HealthStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    HealthStatusCheckedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
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
                    table.PrimaryKey("PK_AIProviders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AIProviders_AIModels_DefaultModelId",
                        column: x => x.DefaultModelId,
                        principalTable: "AIModels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProviderHealthChecks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CheckedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsHealthy = table.Column<bool>(type: "bit", nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_ProviderHealthChecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProviderHealthChecks_AIProviders_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "AIProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserAiPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DefaultProviderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DefaultModelId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DefaultGenerationParametersJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
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
                    table.PrimaryKey("PK_UserAiPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserAiPreferences_AIModels_DefaultModelId",
                        column: x => x.DefaultModelId,
                        principalTable: "AIModels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserAiPreferences_AIProviders_DefaultProviderId",
                        column: x => x.DefaultProviderId,
                        principalTable: "AIProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserAiPreferences_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Baseline seed (research.md Decision 5): administrator-curated starting catalog,
            // all providers disabled until an admin configures a credential (FR-003/FR-004).
            // Not a HasData() entry on the Configuration classes — this is a one-time seed,
            // not a value the model needs to keep reconciling on every future migration.
            var seededAtUtc = new DateTime(2026, 7, 30, 0, 0, 0, DateTimeKind.Utc);

            migrationBuilder.InsertData(
                table: "AIProviders",
                columns: new[] { "Id", "ProviderKey", "DisplayName", "IsEnabled", "HealthStatus", "CreatedAtUtc", "CreatedBy" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-111111111111"), "openai", "OpenAI", false, "Unknown", seededAtUtc, "system:seed" },
                    { new Guid("22222222-2222-2222-2222-222222222222"), "anthropic", "Anthropic", false, "Unknown", seededAtUtc, "system:seed" },
                    { new Guid("33333333-3333-3333-3333-333333333333"), "google-gemini", "Google Gemini", false, "Unknown", seededAtUtc, "system:seed" },
                    { new Guid("44444444-4444-4444-4444-444444444444"), "openrouter", "OpenRouter", false, "Unknown", seededAtUtc, "system:seed" },
                });

            migrationBuilder.InsertData(
                table: "AIModels",
                columns: new[]
                {
                    "Id", "ProviderId", "ModelKey", "DisplayName", "ContextWindowTokens", "MaxOutputTokens",
                    "SupportsStreaming", "SupportsVision", "SupportsFunctionCalling", "SupportsJsonMode",
                    "SupportsReasoning", "SupportsEmbeddings", "SupportsImageInput", "SupportsImageOutput", "SupportsAudio",
                    "Status", "InputPricePerMillionTokensUsd", "OutputPricePerMillionTokensUsd", "CreatedAtUtc", "CreatedBy",
                },
                values: new object[,]
                {
                    { new Guid("aaaaaaaa-0001-0000-0000-000000000000"), new Guid("11111111-1111-1111-1111-111111111111"), "gpt-4.1", "GPT-4.1", 128000, 16384, true, true, true, true, false, false, true, false, false, "Available", 2.50m, 10.00m, seededAtUtc, "system:seed" },
                    { new Guid("aaaaaaaa-0002-0000-0000-000000000000"), new Guid("11111111-1111-1111-1111-111111111111"), "gpt-4o-mini", "GPT-4o mini", 128000, 16384, true, true, true, true, false, false, true, false, false, "Available", 0.15m, 0.60m, seededAtUtc, "system:seed" },
                    { new Guid("bbbbbbbb-0001-0000-0000-000000000000"), new Guid("22222222-2222-2222-2222-222222222222"), "claude-3-5-sonnet-20241022", "Claude 3.5 Sonnet", 200000, 8192, true, true, true, false, false, false, true, false, false, "Available", 3.00m, 15.00m, seededAtUtc, "system:seed" },
                    { new Guid("bbbbbbbb-0002-0000-0000-000000000000"), new Guid("22222222-2222-2222-2222-222222222222"), "claude-3-5-haiku-20241022", "Claude 3.5 Haiku", 200000, 8192, true, false, true, false, false, false, false, false, false, "Available", 0.80m, 4.00m, seededAtUtc, "system:seed" },
                    { new Guid("cccccccc-0001-0000-0000-000000000000"), new Guid("33333333-3333-3333-3333-333333333333"), "gemini-1.5-pro", "Gemini 1.5 Pro", 2000000, 8192, true, true, true, true, false, false, true, false, false, "Available", 1.25m, 5.00m, seededAtUtc, "system:seed" },
                    { new Guid("cccccccc-0002-0000-0000-000000000000"), new Guid("33333333-3333-3333-3333-333333333333"), "gemini-1.5-flash", "Gemini 1.5 Flash", 1000000, 8192, true, true, true, true, false, false, true, false, false, "Available", 0.075m, 0.30m, seededAtUtc, "system:seed" },
                    { new Guid("dddddddd-0001-0000-0000-000000000000"), new Guid("44444444-4444-4444-4444-444444444444"), "openai/gpt-4o", "GPT-4o (via OpenRouter)", 128000, 16384, true, true, true, true, false, false, true, false, false, "Available", 2.50m, 10.00m, seededAtUtc, "system:seed" },
                    { new Guid("dddddddd-0002-0000-0000-000000000000"), new Guid("44444444-4444-4444-4444-444444444444"), "anthropic/claude-3.5-sonnet", "Claude 3.5 Sonnet (via OpenRouter)", 200000, 8192, true, true, true, false, false, false, true, false, false, "Available", 3.00m, 15.00m, seededAtUtc, "system:seed" },
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserChats_ModelId",
                table: "UserChats",
                column: "ModelId");

            migrationBuilder.CreateIndex(
                name: "IX_UserChats_ProviderId",
                table: "UserChats",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ComparisonGroupId",
                table: "Messages",
                column: "ComparisonGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_AIModels_ProviderId_ModelKey",
                table: "AIModels",
                columns: new[] { "ProviderId", "ModelKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AIProviders_DefaultModelId",
                table: "AIProviders",
                column: "DefaultModelId");

            migrationBuilder.CreateIndex(
                name: "IX_AIProviders_ProviderKey",
                table: "AIProviders",
                column: "ProviderKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProviderHealthChecks_ProviderId_CheckedAtUtc",
                table: "ProviderHealthChecks",
                columns: new[] { "ProviderId", "CheckedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UserAiPreferences_DefaultModelId",
                table: "UserAiPreferences",
                column: "DefaultModelId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAiPreferences_DefaultProviderId",
                table: "UserAiPreferences",
                column: "DefaultProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAiPreferences_UserId",
                table: "UserAiPreferences",
                column: "UserId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_UserChats_AIModels_ModelId",
                table: "UserChats",
                column: "ModelId",
                principalTable: "AIModels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserChats_AIProviders_ProviderId",
                table: "UserChats",
                column: "ProviderId",
                principalTable: "AIProviders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AIModels_AIProviders_ProviderId",
                table: "AIModels",
                column: "ProviderId",
                principalTable: "AIProviders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserChats_AIModels_ModelId",
                table: "UserChats");

            migrationBuilder.DropForeignKey(
                name: "FK_UserChats_AIProviders_ProviderId",
                table: "UserChats");

            migrationBuilder.DropForeignKey(
                name: "FK_AIModels_AIProviders_ProviderId",
                table: "AIModels");

            migrationBuilder.DeleteData(table: "AIModels", keyColumn: "Id", keyValue: new Guid("aaaaaaaa-0001-0000-0000-000000000000"));
            migrationBuilder.DeleteData(table: "AIModels", keyColumn: "Id", keyValue: new Guid("aaaaaaaa-0002-0000-0000-000000000000"));
            migrationBuilder.DeleteData(table: "AIModels", keyColumn: "Id", keyValue: new Guid("bbbbbbbb-0001-0000-0000-000000000000"));
            migrationBuilder.DeleteData(table: "AIModels", keyColumn: "Id", keyValue: new Guid("bbbbbbbb-0002-0000-0000-000000000000"));
            migrationBuilder.DeleteData(table: "AIModels", keyColumn: "Id", keyValue: new Guid("cccccccc-0001-0000-0000-000000000000"));
            migrationBuilder.DeleteData(table: "AIModels", keyColumn: "Id", keyValue: new Guid("cccccccc-0002-0000-0000-000000000000"));
            migrationBuilder.DeleteData(table: "AIModels", keyColumn: "Id", keyValue: new Guid("dddddddd-0001-0000-0000-000000000000"));
            migrationBuilder.DeleteData(table: "AIModels", keyColumn: "Id", keyValue: new Guid("dddddddd-0002-0000-0000-000000000000"));
            migrationBuilder.DeleteData(table: "AIProviders", keyColumn: "Id", keyValue: new Guid("11111111-1111-1111-1111-111111111111"));
            migrationBuilder.DeleteData(table: "AIProviders", keyColumn: "Id", keyValue: new Guid("22222222-2222-2222-2222-222222222222"));
            migrationBuilder.DeleteData(table: "AIProviders", keyColumn: "Id", keyValue: new Guid("33333333-3333-3333-3333-333333333333"));
            migrationBuilder.DeleteData(table: "AIProviders", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-444444444444"));

            migrationBuilder.DropTable(
                name: "ProviderHealthChecks");

            migrationBuilder.DropTable(
                name: "UserAiPreferences");

            migrationBuilder.DropTable(
                name: "AIProviders");

            migrationBuilder.DropTable(
                name: "AIModels");

            migrationBuilder.DropIndex(
                name: "IX_UserChats_ModelId",
                table: "UserChats");

            migrationBuilder.DropIndex(
                name: "IX_UserChats_ProviderId",
                table: "UserChats");

            migrationBuilder.DropIndex(
                name: "IX_Messages_ComparisonGroupId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "GenerationParametersJson",
                table: "UserChats");

            migrationBuilder.DropColumn(
                name: "ModelId",
                table: "UserChats");

            migrationBuilder.DropColumn(
                name: "ProviderId",
                table: "UserChats");

            migrationBuilder.DropColumn(
                name: "CachedTokenCount",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ComparisonGroupId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "EstimatedCostUsd",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "IsIncludedInContext",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "LatencyMs",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ReasoningTokenCount",
                table: "Messages");
        }
    }
}
