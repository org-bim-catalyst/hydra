using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddModelDeprecationWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ModelDeprecationBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ConfirmedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserImpactStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UserImpactCompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NotifiedUserCount = table.Column<int>(type: "int", nullable: false),
                    ReplacementChangeOfDeprecationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_ModelDeprecationBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModelDeprecationBatches_AIProviders_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "AIProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ModelDeprecations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ReplacementModelId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReplacementSource = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ValidationStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ValidatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ValidatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
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
                    table.PrimaryKey("PK_ModelDeprecations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModelDeprecations_AIModels_ModelId",
                        column: x => x.ModelId,
                        principalTable: "AIModels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ModelDeprecations_AIModels_ReplacementModelId",
                        column: x => x.ReplacementModelId,
                        principalTable: "AIModels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ModelDeprecations_ModelDeprecationBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "ModelDeprecationBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ItemSwitches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeprecationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ItemKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NodeId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ItemLabel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PreviousModelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NewModelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_ItemSwitches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemSwitches_ModelDeprecationBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "ModelDeprecationBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ItemSwitches_ModelDeprecations_DeprecationId",
                        column: x => x.DeprecationId,
                        principalTable: "ModelDeprecations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlatformDefaultChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeprecationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetKind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ProviderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Capability = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    PreviousModelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    NewModelId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FlagReason = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ServingProviderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ServingModelId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResolvedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolutionModelId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_PlatformDefaultChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformDefaultChanges_AIModels_NewModelId",
                        column: x => x.NewModelId,
                        principalTable: "AIModels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlatformDefaultChanges_AIModels_PreviousModelId",
                        column: x => x.PreviousModelId,
                        principalTable: "AIModels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlatformDefaultChanges_ModelDeprecations_DeprecationId",
                        column: x => x.DeprecationId,
                        principalTable: "ModelDeprecations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItemSwitches_BatchId_OwnerUserId",
                table: "ItemSwitches",
                columns: new[] { "BatchId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemSwitches_DeprecationId",
                table: "ItemSwitches",
                column: "DeprecationId");

            migrationBuilder.CreateIndex(
                name: "IX_ModelDeprecationBatches_ProviderId",
                table: "ModelDeprecationBatches",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_ModelDeprecationBatches_UserImpactStatus_ConfirmedAtUtc",
                table: "ModelDeprecationBatches",
                columns: new[] { "UserImpactStatus", "ConfirmedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ModelDeprecations_BatchId",
                table: "ModelDeprecations",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ModelDeprecations_ModelId",
                table: "ModelDeprecations",
                column: "ModelId",
                unique: true,
                filter: "[DeletedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ModelDeprecations_ReplacementModelId",
                table: "ModelDeprecations",
                column: "ReplacementModelId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformDefaultChanges_DeprecationId",
                table: "PlatformDefaultChanges",
                column: "DeprecationId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformDefaultChanges_NewModelId",
                table: "PlatformDefaultChanges",
                column: "NewModelId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformDefaultChanges_Outcome_ResolvedAtUtc",
                table: "PlatformDefaultChanges",
                columns: new[] { "Outcome", "ResolvedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformDefaultChanges_PreviousModelId",
                table: "PlatformDefaultChanges",
                column: "PreviousModelId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemSwitches");

            migrationBuilder.DropTable(
                name: "PlatformDefaultChanges");

            migrationBuilder.DropTable(
                name: "ModelDeprecations");

            migrationBuilder.DropTable(
                name: "ModelDeprecationBatches");
        }
    }
}
