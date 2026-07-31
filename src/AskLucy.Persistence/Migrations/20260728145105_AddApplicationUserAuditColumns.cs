using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationUserAuditColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "AspNetUsers",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "AspNetUsers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "AspNetUsers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Backfill: no real historical registration date exists for pre-existing rows
            // (research.md Topic 1, accepted approximation) — set them to the moment this
            // migration is applied, rather than leaving the column's placeholder default
            // (DateTime.MinValue) in place, which would distort the 30-day trend chart.
            migrationBuilder.Sql(
                "UPDATE [AspNetUsers] SET [CreatedAtUtc] = SYSUTCDATETIME() WHERE [CreatedAtUtc] = '0001-01-01T00:00:00.0000000';");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_CreatedAtUtc",
                table: "AspNetUsers",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_IsDeleted",
                table: "AspNetUsers",
                column: "IsDeleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_CreatedAtUtc",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_IsDeleted",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "AspNetUsers");
        }
    }
}
