using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddActiveLocationToUserChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "ActiveLocationConfidence",
                table: "UserChats",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ActiveLocationLatitude",
                table: "UserChats",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ActiveLocationLongitude",
                table: "UserChats",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActiveLocationName",
                table: "UserChats",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActiveLocationConfidence",
                table: "UserChats");

            migrationBuilder.DropColumn(
                name: "ActiveLocationLatitude",
                table: "UserChats");

            migrationBuilder.DropColumn(
                name: "ActiveLocationLongitude",
                table: "UserChats");

            migrationBuilder.DropColumn(
                name: "ActiveLocationName",
                table: "UserChats");
        }
    }
}
