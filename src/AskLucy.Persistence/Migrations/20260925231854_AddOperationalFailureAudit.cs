using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationalFailureAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                table: "RoleAuditLogs",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                table: "McpAuditLogs",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OperationalFailureIncidents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GroupingKey = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    RootCauseKey = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    Engine = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ProviderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProviderName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SubjectType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubjectLabel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    HighestSeverity = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OccurrenceCount = table.Column<int>(type: "int", nullable: false),
                    StoredOccurrenceCount = table.Column<int>(type: "int", nullable: false),
                    DistinctUserCount = table.Column<int>(type: "int", nullable: false),
                    DistinctSourceCount = table.Column<int>(type: "int", nullable: false),
                    RecoveryCount = table.Column<int>(type: "int", nullable: false),
                    LatestReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    LatestCorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TriageState = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    AcknowledgedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    AcknowledgedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolvedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolutionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RecurrenceOfIncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_OperationalFailureIncidents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserContentAccessEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ViewerUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    OwnerUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ItemType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IsOwnerErased = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_UserContentAccessEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OperationalFailureIncidentParticipants",
                columns: table => new
                {
                    IncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParticipantType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ParticipantKey = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationalFailureIncidentParticipants", x => new { x.IncidentId, x.ParticipantType, x.ParticipantKey });
                    table.ForeignKey(
                        name: "FK_OperationalFailureIncidentParticipants_OperationalFailureIncidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "OperationalFailureIncidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OperationalFailureOccurrences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Engine = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ProviderName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IsFailover = table.Column<bool>(type: "bit", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ChatId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WorkflowId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WorkflowExecutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WorkflowExecutionNodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    KnowledgeBaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AgentExecutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    McpServerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    JobId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SourceIp = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    IsUserErased = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_OperationalFailureOccurrences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OperationalFailureOccurrences_OperationalFailureIncidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "OperationalFailureIncidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoleAuditLogs_CorrelationId",
                table: "RoleAuditLogs",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_McpAuditLogs_CorrelationId",
                table: "McpAuditLogs",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_Participants_Key",
                table: "OperationalFailureIncidentParticipants",
                columns: new[] { "ParticipantKey", "ParticipantType" })
                .Annotation("SqlServer:Include", new[] { "IncidentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_Badge",
                table: "OperationalFailureIncidents",
                columns: new[] { "HighestSeverity", "TriageState", "RootCauseKey" },
                filter: "[TriageState] = N'Open'");

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_LastSeen",
                table: "OperationalFailureIncidents",
                column: "LastSeenUtc",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_RootCause",
                table: "OperationalFailureIncidents",
                columns: new[] { "RootCauseKey", "TriageState" });

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_State_LastSeen",
                table: "OperationalFailureIncidents",
                columns: new[] { "TriageState", "LastSeenUtc" },
                descending: new[] { false, true })
                .Annotation("SqlServer:Include", new[] { "HighestSeverity", "Engine", "ProviderName", "Kind" });

            migrationBuilder.CreateIndex(
                name: "UX_Incidents_GroupingKey_Unresolved",
                table: "OperationalFailureIncidents",
                column: "GroupingKey",
                unique: true,
                filter: "[TriageState] <> N'Resolved'");

            migrationBuilder.CreateIndex(
                name: "IX_Occurrences_ChatId",
                table: "OperationalFailureOccurrences",
                column: "ChatId",
                filter: "[ChatId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Occurrences_DocumentId",
                table: "OperationalFailureOccurrences",
                column: "DocumentId",
                filter: "[DocumentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Occurrences_Incident_OccurredAt",
                table: "OperationalFailureOccurrences",
                columns: new[] { "IncidentId", "OccurredAtUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Occurrences_OccurredAt",
                table: "OperationalFailureOccurrences",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Occurrences_UserId",
                table: "OperationalFailureOccurrences",
                column: "UserId",
                filter: "[UserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Occurrences_WorkflowExecutionId",
                table: "OperationalFailureOccurrences",
                column: "WorkflowExecutionId",
                filter: "[WorkflowExecutionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserContentAccessEvents_OccurredAtUtc",
                table: "UserContentAccessEvents",
                column: "OccurredAtUtc",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_UserContentAccessEvents_OwnerUserId",
                table: "UserContentAccessEvents",
                column: "OwnerUserId",
                filter: "[OwnerUserId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OperationalFailureIncidentParticipants");

            migrationBuilder.DropTable(
                name: "OperationalFailureOccurrences");

            migrationBuilder.DropTable(
                name: "UserContentAccessEvents");

            migrationBuilder.DropTable(
                name: "OperationalFailureIncidents");

            migrationBuilder.DropIndex(
                name: "IX_RoleAuditLogs_CorrelationId",
                table: "RoleAuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_McpAuditLogs_CorrelationId",
                table: "McpAuditLogs");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "RoleAuditLogs");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "McpAuditLogs");
        }
    }
}
