using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationFullTextSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SQL Server Full-Text Search catalog/indexes (research.md Topic 5) — no fluent
            // API exists for this in EF Core, so it's raw SQL. Population is asynchronous
            // (CHANGE_TRACKING AUTO), which is what gives near-real-time search freshness
            // (SC-001a) without a bespoke indexing pipeline.
            //
            // suppressTransaction: true is required — CREATE FULLTEXT CATALOG/INDEX cannot
            // run inside a transaction. This lives in its own migration (split from
            // AddConversationManagement per EF Core's own tooling warning) so a failure here
            // never leaves an unrelated schema change partially applied.
            migrationBuilder.Sql(
                @"
IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = 'ConversationSearchCatalog')
    CREATE FULLTEXT CATALOG ConversationSearchCatalog AS DEFAULT;

IF NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('UserChats'))
    CREATE FULLTEXT INDEX ON UserChats(Title)
        KEY INDEX PK_UserChats ON ConversationSearchCatalog
        WITH CHANGE_TRACKING AUTO;

IF NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('Messages'))
    CREATE FULLTEXT INDEX ON Messages(Content)
        KEY INDEX PK_Messages ON ConversationSearchCatalog
        WITH CHANGE_TRACKING AUTO;
",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Same suppressTransaction requirement as Up() — DROP FULLTEXT INDEX/CATALOG
            // cannot run inside a transaction either.
            migrationBuilder.Sql(
                @"
IF EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('Messages'))
    DROP FULLTEXT INDEX ON Messages;
IF EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('UserChats'))
    DROP FULLTEXT INDEX ON UserChats;
IF EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = 'ConversationSearchCatalog')
    DROP FULLTEXT CATALOG ConversationSearchCatalog;
",
                suppressTransaction: true);
        }
    }
}
