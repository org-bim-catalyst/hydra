using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteBoundaryCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ActiveBoundaryCorrectionId",
                table: "UserChats",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ActiveBoundaryRevision",
                table: "UserChats",
                type: "uniqueidentifier",
                nullable: true);

            // specs/079: every chat that already shows an outline gets a revision token, so the
            // first edit has something to compare against.
            migrationBuilder.Sql(
                "UPDATE UserChats SET ActiveBoundaryRevision = NEWID() WHERE ActiveBoundarySiteName IS NOT NULL");

            migrationBuilder.CreateTable(
                name: "SiteBoundaryCorrections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    SiteName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    NormalizedSiteName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    FoundCentroidLatitude = table.Column<double>(type: "float", nullable: false),
                    FoundCentroidLongitude = table.Column<double>(type: "float", nullable: false),
                    EditedRingsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AreaSquareMeters = table.Column<double>(type: "float", nullable: false),
                    FoundSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MembersJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Revision = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_SiteBoundaryCorrections", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserChats_ActiveBoundaryCorrectionId",
                table: "UserChats",
                column: "ActiveBoundaryCorrectionId",
                filter: "[ActiveBoundaryCorrectionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SiteBoundaryCorrections_UserId_NormalizedSiteName",
                table: "SiteBoundaryCorrections",
                columns: new[] { "UserId", "NormalizedSiteName" },
                filter: "[DeletedAtUtc] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SiteBoundaryCorrections");

            migrationBuilder.DropIndex(
                name: "IX_UserChats_ActiveBoundaryCorrectionId",
                table: "UserChats");

            migrationBuilder.DropColumn(
                name: "ActiveBoundaryCorrectionId",
                table: "UserChats");

            migrationBuilder.DropColumn(
                name: "ActiveBoundaryRevision",
                table: "UserChats");
        }
    }
}
