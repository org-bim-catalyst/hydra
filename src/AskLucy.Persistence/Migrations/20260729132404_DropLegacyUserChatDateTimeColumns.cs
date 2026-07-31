using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Persistence.Migrations
{
    /// <summary>
    /// Fixes a production-only bug: the legacy pre-adoption schema's <c>CreationDateTime</c>/
    /// <c>LastAccessDateTime</c> columns (NOT NULL, no default) were carried over by the
    /// manual baseline procedure in Migrations/README.md, but the current
    /// <c>AskLucy.Domain.Chats.UserChat</c> model only maps <c>CreatedAtUtc</c>/
    /// <c>ModifiedAtUtc</c> and has no idea these columns exist — so every EF-generated
    /// INSERT omits them, and SQL Server rejects every new chat creation with a NOT NULL
    /// violation. Never reproduced locally because LocalDB's schema came from a clean
    /// InitialCreate that never had these columns to begin with — only a hand-adopted
    /// database (production) can have them. Guarded with IF-EXISTS checks so this migration
    /// is a safe no-op anywhere the columns are already absent (LocalDB, a fresh clone, etc).
    /// </summary>
    public partial class DropLegacyUserChatDateTimeColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF COL_LENGTH('UserChats', 'CreationDateTime') IS NOT NULL
BEGIN
    ALTER TABLE [UserChats] DROP COLUMN [CreationDateTime];
END");

            migrationBuilder.Sql(@"
IF COL_LENGTH('UserChats', 'LastAccessDateTime') IS NOT NULL
BEGIN
    ALTER TABLE [UserChats] DROP COLUMN [LastAccessDateTime];
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF COL_LENGTH('UserChats', 'CreationDateTime') IS NULL
BEGIN
    ALTER TABLE [UserChats] ADD [CreationDateTime] smalldatetime NOT NULL DEFAULT (GETUTCDATE());
END");

            migrationBuilder.Sql(@"
IF COL_LENGTH('UserChats', 'LastAccessDateTime') IS NULL
BEGIN
    ALTER TABLE [UserChats] ADD [LastAccessDateTime] smalldatetime NOT NULL DEFAULT (GETUTCDATE());
END");
        }
    }
}
