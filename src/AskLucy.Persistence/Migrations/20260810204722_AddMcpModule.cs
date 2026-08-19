using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMcpModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ToolName",
                table: "AgentTools",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "ToolName",
                table: "AgentToolCalls",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "ToolName",
                table: "AgentPolicies",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "ToolName",
                table: "AgentExecutionSteps",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "McpAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    McpServerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    FailureCategory = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    DetailsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_McpAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "McpServers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Endpoint = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Transport = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AuthenticationType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RequiresUnauthenticatedConfirmation = table.Column<bool>(type: "bit", nullable: false),
                    AllowInsecureTransport = table.Column<bool>(type: "bit", nullable: false),
                    InsecureTransportJustification = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    EndpointValidationOverride = table.Column<bool>(type: "bit", nullable: false),
                    EndpointValidationJustification = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    OwnerUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ConfigurationVersion = table.Column<int>(type: "int", nullable: false),
                    CapabilityRefreshIntervalMinutes = table.Column<int>(type: "int", nullable: false),
                    LastHealthCheckAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastCapabilityDiscoveryAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
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
                    table.PrimaryKey("PK_McpServers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "McpCapabilitySnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    McpServerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DiscoveredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SnapshotVersion = table.Column<int>(type: "int", nullable: false),
                    DeclaredCapabilitiesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ChangeSummaryJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WasSuccessful = table.Column<bool>(type: "bit", nullable: false),
                    FailureCategory = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
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
                    table.PrimaryKey("PK_McpCapabilitySnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_McpCapabilitySnapshots_McpServers_McpServerId",
                        column: x => x.McpServerId,
                        principalTable: "McpServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "McpServerCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    McpServerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CiphertextBlob = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RotatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RotatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
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
                    table.PrimaryKey("PK_McpServerCredentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_McpServerCredentials_McpServers_McpServerId",
                        column: x => x.McpServerId,
                        principalTable: "McpServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "McpServerHealths",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    McpServerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    FailureCategory = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Detail = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CheckedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConsecutiveFailureCount = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_McpServerHealths", x => x.Id);
                    table.ForeignKey(
                        name: "FK_McpServerHealths_McpServers_McpServerId",
                        column: x => x.McpServerId,
                        principalTable: "McpServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "McpPrompts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    McpServerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    McpCapabilitySnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NamespacedName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ContentTemplate = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsAvailable = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_McpPrompts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_McpPrompts_McpCapabilitySnapshots_McpCapabilitySnapshotId",
                        column: x => x.McpCapabilitySnapshotId,
                        principalTable: "McpCapabilitySnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_McpPrompts_McpServers_McpServerId",
                        column: x => x.McpServerId,
                        principalTable: "McpServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "McpResources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    McpServerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    McpCapabilitySnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NamespacedName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Uri = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ContentType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsAvailable = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_McpResources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_McpResources_McpCapabilitySnapshots_McpCapabilitySnapshotId",
                        column: x => x.McpCapabilitySnapshotId,
                        principalTable: "McpCapabilitySnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_McpResources_McpServers_McpServerId",
                        column: x => x.McpServerId,
                        principalTable: "McpServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "McpTools",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    McpServerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    McpCapabilitySnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NamespacedName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ToolName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    InputSchemaJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OutputSchemaJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DeclaredCapabilitiesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ServerDeclaredRiskLevel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    EffectiveRiskLevel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequiredPermissionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ActivationStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ActivatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ActivatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Version = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsAvailable = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_McpTools", x => x.Id);
                    table.ForeignKey(
                        name: "FK_McpTools_McpCapabilitySnapshots_McpCapabilitySnapshotId",
                        column: x => x.McpCapabilitySnapshotId,
                        principalTable: "McpCapabilitySnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_McpTools_McpServers_McpServerId",
                        column: x => x.McpServerId,
                        principalTable: "McpServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_McpAuditLogs_Action",
                table: "McpAuditLogs",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_McpAuditLogs_McpServerId",
                table: "McpAuditLogs",
                column: "McpServerId");

            migrationBuilder.CreateIndex(
                name: "IX_McpAuditLogs_UserId",
                table: "McpAuditLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_McpCapabilitySnapshots_McpServerId_SnapshotVersion",
                table: "McpCapabilitySnapshots",
                columns: new[] { "McpServerId", "SnapshotVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_McpPrompts_McpCapabilitySnapshotId",
                table: "McpPrompts",
                column: "McpCapabilitySnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_McpPrompts_McpServerId",
                table: "McpPrompts",
                column: "McpServerId");

            migrationBuilder.CreateIndex(
                name: "IX_McpPrompts_NamespacedName",
                table: "McpPrompts",
                column: "NamespacedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_McpResources_McpCapabilitySnapshotId",
                table: "McpResources",
                column: "McpCapabilitySnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_McpResources_McpServerId",
                table: "McpResources",
                column: "McpServerId");

            migrationBuilder.CreateIndex(
                name: "IX_McpResources_NamespacedName",
                table: "McpResources",
                column: "NamespacedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_McpServerCredentials_McpServerId",
                table: "McpServerCredentials",
                column: "McpServerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_McpServerHealths_McpServerId",
                table: "McpServerHealths",
                column: "McpServerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_McpServers_Endpoint_Transport",
                table: "McpServers",
                columns: new[] { "Endpoint", "Transport" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_McpServers_IsEnabled",
                table: "McpServers",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_McpServers_OwnerUserId",
                table: "McpServers",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_McpTools_McpCapabilitySnapshotId",
                table: "McpTools",
                column: "McpCapabilitySnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_McpTools_McpServerId",
                table: "McpTools",
                column: "McpServerId");

            migrationBuilder.CreateIndex(
                name: "IX_McpTools_McpServerId_ActivationStatus_IsAvailable",
                table: "McpTools",
                columns: new[] { "McpServerId", "ActivationStatus", "IsAvailable" });

            migrationBuilder.CreateIndex(
                name: "IX_McpTools_NamespacedName",
                table: "McpTools",
                column: "NamespacedName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "McpAuditLogs");

            migrationBuilder.DropTable(
                name: "McpPrompts");

            migrationBuilder.DropTable(
                name: "McpResources");

            migrationBuilder.DropTable(
                name: "McpServerCredentials");

            migrationBuilder.DropTable(
                name: "McpServerHealths");

            migrationBuilder.DropTable(
                name: "McpTools");

            migrationBuilder.DropTable(
                name: "McpCapabilitySnapshots");

            migrationBuilder.DropTable(
                name: "McpServers");

            migrationBuilder.AlterColumn<string>(
                name: "ToolName",
                table: "AgentTools",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(400)",
                oldMaxLength: 400);

            migrationBuilder.AlterColumn<string>(
                name: "ToolName",
                table: "AgentToolCalls",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(400)",
                oldMaxLength: 400);

            migrationBuilder.AlterColumn<string>(
                name: "ToolName",
                table: "AgentPolicies",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(400)",
                oldMaxLength: 400);

            migrationBuilder.AlterColumn<string>(
                name: "ToolName",
                table: "AgentExecutionSteps",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(400)",
                oldMaxLength: 400,
                oldNullable: true);
        }
    }
}
