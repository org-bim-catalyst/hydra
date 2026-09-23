using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomModels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, collation: "SQL_Latin1_General_CP1_CI_AS"),
                    RepositoryId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, collation: "SQL_Latin1_General_CP1_CI_AS"),
                    Revision = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ResolvedCommitSha = table.Column<string>(type: "nchar(40)", fixedLength: true, maxLength: 40, nullable: true),
                    SourceUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    Destination = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false, collation: "SQL_Latin1_General_CP1_CI_AS"),
                    DeploymentState = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Availability = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TotalBytes = table.Column<long>(type: "bigint", nullable: true),
                    TransferredBytes = table.Column<long>(type: "bigint", nullable: false),
                    TotalFileCount = table.Column<int>(type: "int", nullable: true),
                    CompletedFileCount = table.Column<int>(type: "int", nullable: false),
                    CurrentFilePath = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    CurrentFileBytes = table.Column<long>(type: "bigint", nullable: true),
                    CurrentFileTotalBytes = table.Column<long>(type: "bigint", nullable: true),
                    FailureKind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SubmittedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CancellationRequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    BackgroundJobId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FinishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsInProgress = table.Column<bool>(type: "bit", nullable: false),
                    OverwrittenFileCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomModels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomModelOverwrittenFiles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomModelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RelativePath = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    PreviousSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    OverwrittenAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomModelOverwrittenFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomModelOverwrittenFiles_CustomModels_CustomModelId",
                        column: x => x.CustomModelId,
                        principalTable: "CustomModels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomModelOverwrittenFiles_CustomModelId",
                table: "CustomModelOverwrittenFiles",
                column: "CustomModelId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomModels_CreatedAtUtc",
                table: "CustomModels",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CustomModels_RepositoryId_DeploymentState",
                table: "CustomModels",
                columns: new[] { "RepositoryId", "DeploymentState" });

            migrationBuilder.CreateIndex(
                name: "UX_CustomModels_Destination_InProgress",
                table: "CustomModels",
                column: "Destination",
                unique: true,
                filter: "[IsInProgress] = 1");

            migrationBuilder.CreateIndex(
                name: "UX_CustomModels_Name",
                table: "CustomModels",
                column: "Name",
                unique: true,
                filter: "[DeletedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_CustomModels_Repository_Available",
                table: "CustomModels",
                column: "RepositoryId",
                unique: true,
                filter: "[Availability] = 'Available' AND [DeletedAtUtc] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomModelOverwrittenFiles");

            migrationBuilder.DropTable(
                name: "CustomModels");
        }
    }
}
