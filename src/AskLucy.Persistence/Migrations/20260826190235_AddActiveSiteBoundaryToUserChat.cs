using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddActiveSiteBoundaryToUserChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "ActiveBoundaryAreaSquareMeters",
                table: "UserChats",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ActiveBoundaryCentroidLatitude",
                table: "UserChats",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ActiveBoundaryCentroidLongitude",
                table: "UserChats",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ActiveBoundaryConfidence",
                table: "UserChats",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActiveBoundaryConfidenceLevel",
                table: "UserChats",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActiveBoundaryPolygonJson",
                table: "UserChats",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActiveBoundarySiteName",
                table: "UserChats",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActiveBoundarySource",
                table: "UserChats",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActiveBoundarySourceDetail",
                table: "UserChats",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActiveBoundaryAreaSquareMeters",
                table: "UserChats");

            migrationBuilder.DropColumn(
                name: "ActiveBoundaryCentroidLatitude",
                table: "UserChats");

            migrationBuilder.DropColumn(
                name: "ActiveBoundaryCentroidLongitude",
                table: "UserChats");

            migrationBuilder.DropColumn(
                name: "ActiveBoundaryConfidence",
                table: "UserChats");

            migrationBuilder.DropColumn(
                name: "ActiveBoundaryConfidenceLevel",
                table: "UserChats");

            migrationBuilder.DropColumn(
                name: "ActiveBoundaryPolygonJson",
                table: "UserChats");

            migrationBuilder.DropColumn(
                name: "ActiveBoundarySiteName",
                table: "UserChats");

            migrationBuilder.DropColumn(
                name: "ActiveBoundarySource",
                table: "UserChats");

            migrationBuilder.DropColumn(
                name: "ActiveBoundarySourceDetail",
                table: "UserChats");
        }
    }
}
