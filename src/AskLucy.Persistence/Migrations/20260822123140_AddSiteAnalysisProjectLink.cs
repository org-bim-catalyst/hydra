using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteAnalysisProjectLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SiteAnalysisProjectLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserChatId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TheDigitalCoreProjectId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LinkSource = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SiteName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ResolvedLatitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    ResolvedLongitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
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
                    table.PrimaryKey("PK_SiteAnalysisProjectLinks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SiteAnalysisProjectLinks_TheDigitalCoreProjectId",
                table: "SiteAnalysisProjectLinks",
                column: "TheDigitalCoreProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteAnalysisProjectLinks_UserChatId",
                table: "SiteAnalysisProjectLinks",
                column: "UserChatId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SiteAnalysisProjectLinks");
        }
    }
}
