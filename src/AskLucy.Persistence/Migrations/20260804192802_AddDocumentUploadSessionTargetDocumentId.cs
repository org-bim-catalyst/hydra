using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentUploadSessionTargetDocumentId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TargetDocumentId",
                table: "DocumentUploadSessions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentUploadSessions_TargetDocumentId_Status",
                table: "DocumentUploadSessions",
                columns: new[] { "TargetDocumentId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DocumentUploadSessions_TargetDocumentId_Status",
                table: "DocumentUploadSessions");

            migrationBuilder.DropColumn(
                name: "TargetDocumentId",
                table: "DocumentUploadSessions");
        }
    }
}
