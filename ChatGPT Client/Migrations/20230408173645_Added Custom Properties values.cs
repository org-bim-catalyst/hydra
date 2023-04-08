using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Migrations
{
    /// <inheritdoc />
    public partial class AddedCustomPropertiesvalues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "0eb8f096-33c7-45c5-9160-fd9cdd053e97",
                columns: new[] { "BirthDate", "ConcurrencyStamp", "EmailConfirmed", "FirstName", "LastName", "PasswordHash", "SecurityStamp" },
                values: new object[] { new DateTime(1981, 9, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "e5f0b03b-7af5-4720-8bd0-f6e33b36386c", true, "Mustafa", "Ali", "AQAAAAIAAYagAAAAEBJQ6BLTl5fYL1oRf8FbjvQaBtVAKJH5QtA1RXfkzf0RZsoLoF8WygBIT32FCLHmWg==", "c00060b7-64ec-450c-8304-dc5dd56cfbb2" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "0eb8f096-33c7-45c5-9160-fd9cdd053e97",
                columns: new[] { "BirthDate", "ConcurrencyStamp", "EmailConfirmed", "FirstName", "LastName", "PasswordHash", "SecurityStamp" },
                values: new object[] { null, "148c4ad0-981c-45df-9b4f-a0473fa72080", false, null, null, "AQAAAAIAAYagAAAAEMNbIpjxTGq9ySavEMBHIaV+Pbm2LpKc4j2OHoefPx/8S4rDQAURwiZ5nZsDWrRPtg==", "5dc94765-56b3-4b72-b70c-fbc267b954e1" });
        }
    }
}
