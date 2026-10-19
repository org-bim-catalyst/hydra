using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderHealthRetentionSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProviderHealthRetentionAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    PreviousRetentionDays = table.Column<int>(type: "int", nullable: false),
                    NewRetentionDays = table.Column<int>(type: "int", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderHealthRetentionAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProviderHealthRetentionSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RetentionDays = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderHealthRetentionSettings", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "ProviderHealthRetentionSettings",
                columns: new[] { "Id", "CreatedAtUtc", "CreatedBy", "DeletedAtUtc", "DeletedBy", "ModifiedAtUtc", "ModifiedBy", "RetentionDays" },
                values: new object[] { new Guid("3a9f0b21-6c4d-4e87-b5a2-082000000001"), new DateTime(2026, 10, 19, 0, 0, 0, 0, DateTimeKind.Utc), "system", null, null, null, null, 30 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProviderHealthRetentionAuditLogs");

            migrationBuilder.DropTable(
                name: "ProviderHealthRetentionSettings");
        }
    }
}
