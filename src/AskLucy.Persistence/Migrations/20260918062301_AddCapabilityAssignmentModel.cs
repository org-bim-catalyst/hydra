using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCapabilityAssignmentModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ModelId",
                table: "AiCapabilityAssignments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiCapabilityAssignments_ModelId",
                table: "AiCapabilityAssignments",
                column: "ModelId");

            migrationBuilder.AddForeignKey(
                name: "FK_AiCapabilityAssignments_AIModels_ModelId",
                table: "AiCapabilityAssignments",
                column: "ModelId",
                principalTable: "AIModels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // One-time backfill. Model sync never marked any model as producing images (OpenAI
            // publishes no capability metadata; the Gemini mapping hard-coded false), and sync only
            // adds/retires rows — it never refreshes an existing one — so image models already in
            // the catalogue would stay unassignable to the new ImageGeneration capability. Same
            // naming rule the providers' sync now applies to newly listed models. Intentionally not
            // reverted in Down: the flag is true for these models regardless of this migration.
            migrationBuilder.Sql(
                "UPDATE AIModels SET SupportsImageOutput = 1 " +
                "WHERE SupportsImageOutput = 0 AND (ModelKey LIKE 'gpt-image%' OR ModelKey LIKE 'dall-e%' OR (ModelKey LIKE 'gemini%' AND ModelKey LIKE '%-image%'));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AiCapabilityAssignments_AIModels_ModelId",
                table: "AiCapabilityAssignments");

            migrationBuilder.DropIndex(
                name: "IX_AiCapabilityAssignments_ModelId",
                table: "AiCapabilityAssignments");

            migrationBuilder.DropColumn(
                name: "ModelId",
                table: "AiCapabilityAssignments");
        }
    }
}
