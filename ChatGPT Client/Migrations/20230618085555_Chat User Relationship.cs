using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Migrations
{
    /// <inheritdoc />
    public partial class ChatUserRelationship : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "0eb8f096-33c7-45c5-9160-fd9cdd053e97",
                columns: new[] { "ConcurrencyStamp", "PasswordHash", "SecurityStamp" },
                values: new object[] { "899b2583-11d0-4be6-9895-6b59b13c3856", "AQAAAAIAAYagAAAAEAOyoS3UVAdiN7ni8FgpkQIWsfg4ZveWy94UmfDkCpy5WqggUBnkIBx9Z4d8vv+xqg==", "12669264-3600-4d0a-83fe-e9456195550c" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "0eb8f096-33c7-45c5-9160-fd9cdd053e97",
                columns: new[] { "ConcurrencyStamp", "PasswordHash", "SecurityStamp" },
                values: new object[] { "9f2efd33-d9d3-4535-93a7-51fbd73a9e60", "AQAAAAIAAYagAAAAEE7TwCd8XsIn9xOH0Ge/qZOCIDkoKGkesXZFrCHp8MAf3yocUKRI2l/VCMa1K1w9OQ==", "0d14738d-e8aa-4917-9b6a-be28c09bb8ae" });
        }
    }
}
