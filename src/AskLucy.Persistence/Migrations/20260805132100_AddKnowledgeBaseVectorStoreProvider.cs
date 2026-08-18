using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeBaseVectorStoreProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VectorStoreProvider",
                table: "KnowledgeBases",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "SqlServer");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VectorStoreProvider",
                table: "KnowledgeBases");
        }
    }
}
