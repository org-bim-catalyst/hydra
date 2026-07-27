using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Migrations
{
    /// <inheritdoc />
    public partial class UserChats1tn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_UserChats_UserId",
                table: "UserChats",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_UserChats_AspNetUsers_UserId",
                table: "UserChats",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserChats_AspNetUsers_UserId",
                table: "UserChats");

            migrationBuilder.DropIndex(
                name: "IX_UserChats_UserId",
                table: "UserChats");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "UserChats");

            migrationBuilder.AddColumn<string>(
                name: "ApplicationUserId",
                table: "UserChats",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "0eb8f096-33c7-45c5-9160-fd9cdd053e97",
                columns: new[] { "ConcurrencyStamp", "PasswordHash", "SecurityStamp" },
                values: new object[] { "899b2583-11d0-4be6-9895-6b59b13c3856", "AQAAAAIAAYagAAAAEAOyoS3UVAdiN7ni8FgpkQIWsfg4ZveWy94UmfDkCpy5WqggUBnkIBx9Z4d8vv+xqg==", "12669264-3600-4d0a-83fe-e9456195550c" });

            migrationBuilder.CreateIndex(
                name: "IX_UserChats_ApplicationUserId",
                table: "UserChats",
                column: "ApplicationUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_UserChats_AspNetUsers_ApplicationUserId",
                table: "UserChats",
                column: "ApplicationUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }
    }
}
