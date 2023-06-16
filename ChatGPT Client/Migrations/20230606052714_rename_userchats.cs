using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Migrations
{
    /// <inheritdoc />
    public partial class rename_userchats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserChat_AspNetUsers_ApplicationUserId",
                table: "UserChat");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UserChat",
                table: "UserChat");

            migrationBuilder.RenameTable(
                name: "UserChat",
                newName: "UserChats");

            migrationBuilder.RenameIndex(
                name: "IX_UserChat_ApplicationUserId",
                table: "UserChats",
                newName: "IX_UserChats_ApplicationUserId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserChats",
                table: "UserChats",
                column: "Id");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "0eb8f096-33c7-45c5-9160-fd9cdd053e97",
                columns: new[] { "ConcurrencyStamp", "PasswordHash", "SecurityStamp" },
                values: new object[] { "f53f6d34-4dc9-4caa-96a9-c3dd58fef862", "AQAAAAIAAYagAAAAEKOOrwfHjcwzEFctSgbinsKUQkp655oZRriwKP29PsjL7m0/RLYTK7R/05RKYwxS1A==", "d1aca1f1-845e-4905-85d7-86dcf2b7ec64" });

            migrationBuilder.AddForeignKey(
                name: "FK_UserChats_AspNetUsers_ApplicationUserId",
                table: "UserChats",
                column: "ApplicationUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserChats_AspNetUsers_ApplicationUserId",
                table: "UserChats");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UserChats",
                table: "UserChats");

            migrationBuilder.RenameTable(
                name: "UserChats",
                newName: "UserChat");

            migrationBuilder.RenameIndex(
                name: "IX_UserChats_ApplicationUserId",
                table: "UserChat",
                newName: "IX_UserChat_ApplicationUserId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserChat",
                table: "UserChat",
                column: "Id");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "0eb8f096-33c7-45c5-9160-fd9cdd053e97",
                columns: new[] { "ConcurrencyStamp", "PasswordHash", "SecurityStamp" },
                values: new object[] { "7d0630ed-ea0d-493d-bc7c-fbfeed2b37a1", "AQAAAAIAAYagAAAAEFnC4D1zzkQUxIXXz6oAQgh3GgauzXb4MRnz0UvQP5+SD5DjXLDsAQ//o1HgCWZJoQ==", "cf5cbf09-395b-455a-9f8e-3e0ed4ee8435" });

            migrationBuilder.AddForeignKey(
                name: "FK_UserChat_AspNetUsers_ApplicationUserId",
                table: "UserChat",
                column: "ApplicationUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }
    }
}
