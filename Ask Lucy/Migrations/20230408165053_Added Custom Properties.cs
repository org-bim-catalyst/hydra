using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Migrations
{
    /// <inheritdoc />
    public partial class AddedCustomProperties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "BirthDate",
                table: "AspNetUsers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "ProfilePicture",
                table: "AspNetUsers",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "0eb8f096-33c7-45c5-9160-fd9cdd053e97",
                columns: new[] { "BirthDate", "ConcurrencyStamp", "FirstName", "LastName", "PasswordHash", "ProfilePicture", "SecurityStamp" },
                values: new object[] { null, "148c4ad0-981c-45df-9b4f-a0473fa72080", null, null, "AQAAAAIAAYagAAAAEMNbIpjxTGq9ySavEMBHIaV+Pbm2LpKc4j2OHoefPx/8S4rDQAURwiZ5nZsDWrRPtg==", null, "5dc94765-56b3-4b72-b70c-fbc267b954e1" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BirthDate",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "FirstName",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "LastName",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ProfilePicture",
                table: "AspNetUsers");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "0eb8f096-33c7-45c5-9160-fd9cdd053e97",
                columns: new[] { "ConcurrencyStamp", "PasswordHash", "SecurityStamp" },
                values: new object[] { "624e138d-2aec-4f0f-9fb5-7aaca1801fde", "AQAAAAIAAYagAAAAEF06UtPdfndJslK2tgt59jXZguuofwLLgmxNV09LGDi9a3hwgrqiZNg2omYlIpXJMA==", "c41f5682-048d-45b1-928b-6b9ab26e4d68" });
        }
    }
}
