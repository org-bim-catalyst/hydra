using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPromptFullTextSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SQL Server Full-Text Search catalog/index (research.md Decision 12) — no fluent
            // API exists for this in EF Core, so it's raw SQL, mirroring
            // 20260729190610_AddConversationFullTextSearch.cs exactly. Split into its own
            // migration (same reasoning as that one) so a failure here never leaves the table
            // creation above partially applied.
            //
            // suppressTransaction: true is required — CREATE FULLTEXT CATALOG/INDEX cannot run
            // inside a transaction.
            migrationBuilder.Sql(
                @"
IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = 'PromptSearchCatalog')
    CREATE FULLTEXT CATALOG PromptSearchCatalog AS DEFAULT;

IF NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('Prompts'))
    CREATE FULLTEXT INDEX ON Prompts(Name, Description, SystemInstructions, UserInstructions)
        KEY INDEX PK_Prompts ON PromptSearchCatalog
        WITH CHANGE_TRACKING AUTO;
",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Same suppressTransaction requirement as Up() — DROP FULLTEXT INDEX/CATALOG cannot
            // run inside a transaction either.
            migrationBuilder.Sql(
                @"
IF EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('Prompts'))
    DROP FULLTEXT INDEX ON Prompts;
IF EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = 'PromptSearchCatalog')
    DROP FULLTEXT CATALOG PromptSearchCatalog;
",
                suppressTransaction: true);
        }
    }
}
