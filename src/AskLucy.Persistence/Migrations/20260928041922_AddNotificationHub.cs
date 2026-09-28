using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationHub : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NotificationAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Action = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    TargetType = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    TargetId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DetailsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
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
                    table.PrimaryKey("PK_NotificationAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationOutboxEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EventKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RecipientJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    VariablesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RelatedItemType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    RelatedItemId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RelatedItemParentId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ExplicitLanguage = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    LeaseOwner = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FanOutCursor = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
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
                    table.PrimaryKey("PK_NotificationOutboxEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    Frequency = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
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
                    table.PrimaryKey("PK_NotificationPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationPreferences_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SystemAnnouncements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Audience = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TargetRoleIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsCritical = table.Column<bool>(type: "bit", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PublishedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    RecipientCount = table.Column<int>(type: "int", nullable: true),
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
                    table.PrimaryKey("PK_SystemAnnouncements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SystemAnnouncements_AspNetUsers_PublishedByUserId",
                        column: x => x.PublishedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NotificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RecipientKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RecipientAddress = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    Language = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    TemplateVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    MaxAttempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LeaseOwner = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SkipReason = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    FailureKind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ProviderResponse = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeliveredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
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
                    table.PrimaryKey("PK_NotificationDeliveries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipientUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Category = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Language = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    TemplateVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RelatedItemType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    RelatedItemId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ActionRoute = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ActionLabel = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EventKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SourceEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ShowInCenter = table.Column<bool>(type: "bit", nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
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
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_AspNetUsers_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Notifications_NotificationOutboxEvents_SourceEventId",
                        column: x => x.SourceEventId,
                        principalTable: "NotificationOutboxEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "NotificationTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Language = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PublishedVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_NotificationTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationTemplateVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Preheader = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Greeting = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Heading = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BodyParagraphsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SafetyNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FooterNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ActionLabel = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    UsedVariablesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PublishedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ArchivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ArchivedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
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
                    table.PrimaryKey("PK_NotificationTemplateVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationTemplateVersions_NotificationTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "NotificationTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationAuditLogs_OccurredAt",
                table: "NotificationAuditLogs",
                column: "OccurredAtUtc",
                descending: Array.Empty<bool>());

            migrationBuilder.CreateIndex(
                name: "IX_NotificationAuditLogs_Target",
                table: "NotificationAuditLogs",
                columns: new[] { "TargetType", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_Failed",
                table: "NotificationDeliveries",
                columns: new[] { "Status", "LastAttemptAtUtc" },
                descending: new[] { false, true },
                filter: "[Status] IN (N'Failed', N'DeadLettered')");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_Queue",
                table: "NotificationDeliveries",
                columns: new[] { "Status", "NextAttemptAtUtc" },
                filter: "[Status] IN (N'Pending', N'Retrying', N'Sending')")
                .Annotation("SqlServer:Include", new[] { "Priority", "Channel", "LeaseExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_TemplateVersionId",
                table: "NotificationDeliveries",
                column: "TemplateVersionId");

            migrationBuilder.CreateIndex(
                name: "UX_NotificationDeliveries_Notification_Channel",
                table: "NotificationDeliveries",
                columns: new[] { "NotificationId", "Channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutboxEvents_Due",
                table: "NotificationOutboxEvents",
                columns: new[] { "Status", "NextAttemptAtUtc", "OccurredAtUtc" },
                filter: "[Status] IN (N'Pending', N'Processing')")
                .Annotation("SqlServer:Include", new[] { "LeaseExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutboxEvents_EventKey",
                table: "NotificationOutboxEvents",
                column: "EventKey",
                filter: "[EventKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutboxEvents_Processed",
                table: "NotificationOutboxEvents",
                column: "ProcessedAtUtc",
                filter: "[Status] = N'Completed'");

            migrationBuilder.CreateIndex(
                name: "UX_NotificationPreferences_User_Category_Channel",
                table: "NotificationPreferences",
                columns: new[] { "UserId", "Category", "Channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Recipient_Center",
                table: "Notifications",
                columns: new[] { "RecipientUserId", "CreatedAtUtc", "Id" },
                descending: new[] { false, true, true },
                filter: "[DeletedAtUtc] IS NULL AND [ShowInCenter] = 1")
                .Annotation("SqlServer:Include", new[] { "Category", "ReadAtUtc", "Priority", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Recipient_Unread",
                table: "Notifications",
                column: "RecipientUserId",
                filter: "[ReadAtUtc] IS NULL AND [DeletedAtUtc] IS NULL AND [ShowInCenter] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Retention_Deleted",
                table: "Notifications",
                column: "DeletedAtUtc",
                filter: "[DeletedAtUtc] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Retention_Read",
                table: "Notifications",
                column: "ReadAtUtc",
                filter: "[ReadAtUtc] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_SourceEventId",
                table: "Notifications",
                column: "SourceEventId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_TemplateVersionId",
                table: "Notifications",
                column: "TemplateVersionId");

            migrationBuilder.CreateIndex(
                name: "UX_Notifications_Address_EventKey",
                table: "Notifications",
                column: "EventKey",
                unique: true,
                filter: "[EventKey] IS NOT NULL AND [RecipientUserId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Notifications_Recipient_EventKey",
                table: "Notifications",
                columns: new[] { "RecipientUserId", "EventKey" },
                unique: true,
                filter: "[EventKey] IS NOT NULL AND [RecipientUserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationTemplates_PublishedVersionId",
                table: "NotificationTemplates",
                column: "PublishedVersionId");

            migrationBuilder.CreateIndex(
                name: "UX_NotificationTemplates_Type_Channel_Language",
                table: "NotificationTemplates",
                columns: new[] { "Type", "Channel", "Language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_NotificationTemplateVersions_OnePublished",
                table: "NotificationTemplateVersions",
                column: "TemplateId",
                unique: true,
                filter: "[Status] = N'Published'");

            migrationBuilder.CreateIndex(
                name: "UX_NotificationTemplateVersions_Template_Version",
                table: "NotificationTemplateVersions",
                columns: new[] { "TemplateId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SystemAnnouncements_PublishedAt",
                table: "SystemAnnouncements",
                column: "PublishedAtUtc",
                descending: Array.Empty<bool>());

            migrationBuilder.CreateIndex(
                name: "IX_SystemAnnouncements_PublishedByUserId",
                table: "SystemAnnouncements",
                column: "PublishedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_NotificationDeliveries_NotificationTemplateVersions_TemplateVersionId",
                table: "NotificationDeliveries",
                column: "TemplateVersionId",
                principalTable: "NotificationTemplateVersions",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_NotificationDeliveries_Notifications_NotificationId",
                table: "NotificationDeliveries",
                column: "NotificationId",
                principalTable: "Notifications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_NotificationTemplateVersions_TemplateVersionId",
                table: "Notifications",
                column: "TemplateVersionId",
                principalTable: "NotificationTemplateVersions",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_NotificationTemplates_NotificationTemplateVersions_PublishedVersionId",
                table: "NotificationTemplates",
                column: "PublishedVersionId",
                principalTable: "NotificationTemplateVersions",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_NotificationTemplates_NotificationTemplateVersions_PublishedVersionId",
                table: "NotificationTemplates");

            migrationBuilder.DropTable(
                name: "NotificationAuditLogs");

            migrationBuilder.DropTable(
                name: "NotificationDeliveries");

            migrationBuilder.DropTable(
                name: "NotificationPreferences");

            migrationBuilder.DropTable(
                name: "SystemAnnouncements");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "NotificationOutboxEvents");

            migrationBuilder.DropTable(
                name: "NotificationTemplateVersions");

            migrationBuilder.DropTable(
                name: "NotificationTemplates");
        }
    }
}
