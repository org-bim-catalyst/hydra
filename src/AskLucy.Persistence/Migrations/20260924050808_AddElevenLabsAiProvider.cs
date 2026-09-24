using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddElevenLabsAiProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every existing provider answers in conversation, so they all start as Language.
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "AIProviders",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Language");

            // ElevenLabs moves under Admin → AI providers as a Speech vendor: that row becomes the
            // one key and on/off switch for its voice engine and live dictation. Seeded switched
            // off, so nothing calls a lapsed subscription until an administrator turns it on.
            var seededAtUtc = new DateTime(2026, 9, 24, 0, 0, 0, DateTimeKind.Utc);

            migrationBuilder.InsertData(
                table: "AIProviders",
                columns: new[] { "Id", "ProviderKey", "DisplayName", "IsEnabled", "Kind", "HealthStatus", "CreatedAtUtc", "CreatedBy" },
                values: new object[] { new Guid("55555555-5555-5555-5555-555555555555"), "elevenlabs", "ElevenLabs", false, "Speech", "Unknown", seededAtUtc, "system:seed" });

            // Carry over a key already saved on the voice page so the administrator doesn't have
            // to enter it again. Both columns hold the same protector's ciphertext.
            migrationBuilder.Sql(
                """
                UPDATE p
                SET p.CredentialCiphertext = v.CredentialCiphertext,
                    p.CredentialHint = v.CredentialHint,
                    p.CredentialLastRotatedAtUtc = v.CredentialLastRotatedAtUtc
                FROM AIProviders p
                INNER JOIN VoiceProviders v ON v.ProviderKey = 'ElevenLabs'
                WHERE p.ProviderKey = 'elevenlabs' AND v.CredentialCiphertext IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AIProviders",
                keyColumn: "Id",
                keyValue: new Guid("55555555-5555-5555-5555-555555555555"));

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "AIProviders");
        }
    }
}
