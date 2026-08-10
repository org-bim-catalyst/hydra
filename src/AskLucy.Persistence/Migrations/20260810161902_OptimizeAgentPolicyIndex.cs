using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeAgentPolicyIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AgentPolicies_IsEnabled",
                table: "AgentPolicies");

            migrationBuilder.DropIndex(
                name: "IX_AgentPolicies_ToolName",
                table: "AgentPolicies");

            migrationBuilder.CreateIndex(
                name: "IX_AgentPolicies_ToolName_IsEnabled",
                table: "AgentPolicies",
                columns: new[] { "ToolName", "IsEnabled" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AgentPolicies_ToolName_IsEnabled",
                table: "AgentPolicies");

            migrationBuilder.CreateIndex(
                name: "IX_AgentPolicies_IsEnabled",
                table: "AgentPolicies",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_AgentPolicies_ToolName",
                table: "AgentPolicies",
                column: "ToolName");
        }
    }
}
