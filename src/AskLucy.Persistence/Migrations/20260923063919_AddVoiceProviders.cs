using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVoiceProviders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VoiceProviders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    DefaultVoiceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CredentialCiphertext = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CredentialHint = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CredentialLastRotatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
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
                    table.PrimaryKey("PK_VoiceProviders", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VoiceProviders_Priority",
                table: "VoiceProviders",
                column: "Priority");

            migrationBuilder.CreateIndex(
                name: "IX_VoiceProviders_ProviderKey",
                table: "VoiceProviders",
                column: "ProviderKey",
                unique: true);

            // specs/070: ElevenLabs was Lucy's only voice before this table existed, so it is seeded
            // as the primary (Priority 0) with no credential — the configured ElevenLabs:ApiKey keeps
            // working as its fallback. Nothing changes until an administrator adds Supertonic and
            // makes it primary.
            var seededAtUtc = new DateTime(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc);

            migrationBuilder.InsertData(
                table: "VoiceProviders",
                columns: new[] { "Id", "ProviderKey", "DisplayName", "Priority", "CreatedAtUtc", "CreatedBy" },
                values: new object[] { new Guid("e1e1e1e1-0069-0000-0000-000000000001"), "ElevenLabs", "ElevenLabs", 0, seededAtUtc, "system:seed" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VoiceProviders");
        }
    }
}
