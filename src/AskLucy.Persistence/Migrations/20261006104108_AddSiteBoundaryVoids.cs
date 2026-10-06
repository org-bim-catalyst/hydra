using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteBoundaryVoids : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EditedVoidsJson",
                table: "SiteBoundaryCorrections",
                type: "nvarchar(max)",
                nullable: false,
                defaultValueSql: "N'[]'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EditedVoidsJson",
                table: "SiteBoundaryCorrections");
        }
    }
}
