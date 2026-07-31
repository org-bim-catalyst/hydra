using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCookieConsent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CookieConsentRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PolicyVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FunctionalAccepted = table.Column<bool>(type: "bit", nullable: false),
                    AnalyticsAccepted = table.Column<bool>(type: "bit", nullable: false),
                    MarketingAccepted = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_CookieConsentRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CookieConsentRecords_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CookieConsentRecords_UserId_CreatedAtUtc",
                table: "CookieConsentRecords",
                columns: new[] { "UserId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CookieConsentRecords");
        }
    }
}
