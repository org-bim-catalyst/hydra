using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Migrations
{
    /// <inheritdoc />
    public partial class user_role_password_hash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "0eb8f096-33c7-45c5-9160-fd9cdd053e97",
                columns: new[] { "ConcurrencyStamp", "PasswordHash", "SecurityStamp" },
                values: new object[] { "25c61635-15e3-404d-b370-5566face4e0e", "AQAAAAIAAYagAAAAEHLJBIWTPW25kTT0mV6QF4CVhHk4dREbMt7RSJJB/wnukJeKSwHeDAt9r6pOYsnH9Q==", "211fac8e-31e3-411b-8dc9-b029ef61c284" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "0eb8f096-33c7-45c5-9160-fd9cdd053e97",
                columns: new[] { "ConcurrencyStamp", "PasswordHash", "SecurityStamp" },
                values: new object[] { "ed9cc70b-dd62-4517-b529-cd9a24addbeb", null, "659833f6-7900-41b9-9b5f-f6cf54fe2f2d" });
        }
    }
}
