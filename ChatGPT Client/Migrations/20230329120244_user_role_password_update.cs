using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Migrations
{
    /// <inheritdoc />
    public partial class user_role_password_update : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "0eb8f096-33c7-45c5-9160-fd9cdd053e97",
                columns: new[] { "ConcurrencyStamp", "PasswordHash", "SecurityStamp" },
                values: new object[] { "fe50dc8e-71ab-481f-8fff-010936b0732f", "AQAAAAIAAYagAAAAENVTMOdVnUziM8r2tFZ5xiDLH2j834KANgJj22giRrhieD8Ow0H3J9qJvbSD/QFVhw==", "3eecf562-70ba-400b-ab2c-bb7e36998968" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "0eb8f096-33c7-45c5-9160-fd9cdd053e97",
                columns: new[] { "ConcurrencyStamp", "PasswordHash", "SecurityStamp" },
                values: new object[] { "25c61635-15e3-404d-b370-5566face4e0e", "AQAAAAIAAYagAAAAEHLJBIWTPW25kTT0mV6QF4CVhHk4dREbMt7RSJJB/wnukJeKSwHeDAt9r6pOYsnH9Q==", "211fac8e-31e3-411b-8dc9-b029ef61c284" });
        }
    }
}
