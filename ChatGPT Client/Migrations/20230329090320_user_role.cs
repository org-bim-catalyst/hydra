using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AskLucy.Migrations
{
    /// <inheritdoc />
    public partial class user_role : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "AspNetRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "Name", "NormalizedName" },
                values: new object[,]
                {
                    { "d12a6772-02ae-41e6-8448-3b19049b313a", "1", "Super User", "Super User" },
                    { "dc656fc4-221b-44ed-9373-47daec554bd1", "2", "Administrator", "Administrator" }
                });

            migrationBuilder.InsertData(
                table: "AspNetUsers",
                columns: new[] { "Id", "AccessFailedCount", "ConcurrencyStamp", "Email", "EmailConfirmed", "LockoutEnabled", "LockoutEnd", "NormalizedEmail", "NormalizedUserName", "PasswordHash", "PhoneNumber", "PhoneNumberConfirmed", "SecurityStamp", "TwoFactorEnabled", "UserName" },
                values: new object[] { "0eb8f096-33c7-45c5-9160-fd9cdd053e97", 0, "ed9cc70b-dd62-4517-b529-cd9a24addbeb", "mustafa.salaheldin@yahoo.com", false, false, null, null, null, null, "00971501342563", false, "659833f6-7900-41b9-9b5f-f6cf54fe2f2d", false, "mustafa.salaheldin@yahoo.com" });

            migrationBuilder.InsertData(
                table: "AspNetUserRoles",
                columns: new[] { "RoleId", "UserId" },
                values: new object[,]
                {
                    { "d12a6772-02ae-41e6-8448-3b19049b313a", "0eb8f096-33c7-45c5-9160-fd9cdd053e97" },
                    { "dc656fc4-221b-44ed-9373-47daec554bd1", "0eb8f096-33c7-45c5-9160-fd9cdd053e97" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AspNetUserRoles",
                keyColumns: new[] { "RoleId", "UserId" },
                keyValues: new object[] { "d12a6772-02ae-41e6-8448-3b19049b313a", "0eb8f096-33c7-45c5-9160-fd9cdd053e97" });

            migrationBuilder.DeleteData(
                table: "AspNetUserRoles",
                keyColumns: new[] { "RoleId", "UserId" },
                keyValues: new object[] { "dc656fc4-221b-44ed-9373-47daec554bd1", "0eb8f096-33c7-45c5-9160-fd9cdd053e97" });

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: "d12a6772-02ae-41e6-8448-3b19049b313a");

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: "dc656fc4-221b-44ed-9373-47daec554bd1");

            migrationBuilder.DeleteData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "0eb8f096-33c7-45c5-9160-fd9cdd053e97");
        }
    }
}
