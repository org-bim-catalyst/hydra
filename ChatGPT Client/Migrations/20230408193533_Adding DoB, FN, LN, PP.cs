using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Migrations
{
    /// <inheritdoc />
    public partial class AddingDoBFNLNPP : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "BirthDate",
                table: "AspNetUsers",
                type: "date",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "0eb8f096-33c7-45c5-9160-fd9cdd053e97",
                columns: new[] { "ConcurrencyStamp", "PasswordHash", "SecurityStamp" },
                values: new object[] { "cd599d89-83dc-44bb-8c59-85f36b412028", "AQAAAAIAAYagAAAAEO0jqF8T5oMcnLhzRZB13MelQkbhPr9spd4CcExZgaM8AHMAU51Ci9HEwQbr0zQCRg==", "a149decd-3ad5-4953-827a-03ed3911f789" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "BirthDate",
                table: "AspNetUsers",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "date");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "0eb8f096-33c7-45c5-9160-fd9cdd053e97",
                columns: new[] { "ConcurrencyStamp", "PasswordHash", "SecurityStamp" },
                values: new object[] { "e5f0b03b-7af5-4720-8bd0-f6e33b36386c", "AQAAAAIAAYagAAAAEBJQ6BLTl5fYL1oRf8FbjvQaBtVAKJH5QtA1RXfkzf0RZsoLoF8WygBIT32FCLHmWg==", "c00060b7-64ec-450c-8304-dc5dd56cfbb2" });
        }
    }
}
