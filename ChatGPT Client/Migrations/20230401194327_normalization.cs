using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Migrations
{
    /// <inheritdoc />
    public partial class normalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "0eb8f096-33c7-45c5-9160-fd9cdd053e97",
                columns: new[] { "ConcurrencyStamp", "NormalizedEmail", "NormalizedUserName", "PasswordHash", "SecurityStamp" },
                values: new object[] { "624e138d-2aec-4f0f-9fb5-7aaca1801fde", "MUSTAFA.SALAHELDIN@YAHOO.COM", "MUSTAFA.SALAHELDIN@YAHOO.COM", "AQAAAAIAAYagAAAAEF06UtPdfndJslK2tgt59jXZguuofwLLgmxNV09LGDi9a3hwgrqiZNg2omYlIpXJMA==", "c41f5682-048d-45b1-928b-6b9ab26e4d68" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "0eb8f096-33c7-45c5-9160-fd9cdd053e97",
                columns: new[] { "ConcurrencyStamp", "NormalizedEmail", "NormalizedUserName", "PasswordHash", "SecurityStamp" },
                values: new object[] { "fe50dc8e-71ab-481f-8fff-010936b0732f", null, null, "AQAAAAIAAYagAAAAENVTMOdVnUziM8r2tFZ5xiDLH2j834KANgJj22giRrhieD8Ow0H3J9qJvbSD/QFVhw==", "3eecf562-70ba-400b-ab2c-bb7e36998968" });
        }
    }
}
