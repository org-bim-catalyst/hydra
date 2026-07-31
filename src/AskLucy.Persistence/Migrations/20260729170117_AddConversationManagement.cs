using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAtUtc",
                table: "UserChats",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFavorite",
                table: "UserChats",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsTitleManuallySet",
                table: "UserChats",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PinnedAtUtc",
                table: "UserChats",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GenerationParametersJson",
                table: "Messages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InputTokenCount",
                table: "Messages",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Model",
                table: "Messages",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OutputTokenCount",
                table: "Messages",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "Messages",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Attachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AccessLocation = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
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
                    table.PrimaryKey("PK_Attachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Attachments_Messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "Messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Citations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceLabel = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SourceReference = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
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
                    table.PrimaryKey("PK_Citations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Citations_Messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "Messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserChats_ArchivedAtUtc",
                table: "UserChats",
                column: "ArchivedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_UserChats_IsFavorite",
                table: "UserChats",
                column: "IsFavorite");

            migrationBuilder.CreateIndex(
                name: "IX_UserChats_PinnedAtUtc",
                table: "UserChats",
                column: "PinnedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_MessageId",
                table: "Attachments",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_Citations_MessageId",
                table: "Citations",
                column: "MessageId");

            // The SQL Server full-text catalog/indexes (research.md Topic 5) moved to their
            // own migration, AddConversationFullTextSearch — CREATE FULLTEXT CATALOG/INDEX
            // cannot run inside a transaction, and EF Core's own tooling warns that mixing a
            // suppressTransaction: true command into a migration that also has transactional
            // schema changes risks a partially-applied migration if it's interrupted. Keeping
            // this migration schema-only (columns/tables/indexes) means it stays atomic.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Attachments");

            migrationBuilder.DropTable(
                name: "Citations");

            migrationBuilder.DropIndex(
                name: "IX_UserChats_ArchivedAtUtc",
                table: "UserChats");

            migrationBuilder.DropIndex(
                name: "IX_UserChats_IsFavorite",
                table: "UserChats");

            migrationBuilder.DropIndex(
                name: "IX_UserChats_PinnedAtUtc",
                table: "UserChats");

            migrationBuilder.DropColumn(
                name: "ArchivedAtUtc",
                table: "UserChats");

            migrationBuilder.DropColumn(
                name: "IsFavorite",
                table: "UserChats");

            migrationBuilder.DropColumn(
                name: "IsTitleManuallySet",
                table: "UserChats");

            migrationBuilder.DropColumn(
                name: "PinnedAtUtc",
                table: "UserChats");

            migrationBuilder.DropColumn(
                name: "GenerationParametersJson",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "InputTokenCount",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "Model",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "OutputTokenCount",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "Messages");
        }
    }
}
