using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Guarded rather than a plain DropIndex: this index is Identity's own default,
            // created once in InitialCreate and never touched since, so it should exist on any
            // database that ran every migration in order — but a database whose schema and
            // __EFMigrationsHistory have drifted out of sync (e.g. built some other way) may not
            // have it. Skipping when absent is always safe; failing here would abort the whole
            // migration over a no-op.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AspNetRoleClaims_RoleId' AND object_id = OBJECT_ID(N'[dbo].[AspNetRoleClaims]'))
                    DROP INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims];
                """);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "AspNetRoles",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "AspNetRoles",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "AspNetRoles",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBuiltIn",
                table: "AspNetRoles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ModifiedAtUtc",
                table: "AspNetRoles",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModifiedBy",
                table: "AspNetRoles",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ClaimValue",
                table: "AspNetRoleClaims",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ClaimType",
                table: "AspNetRoleClaims",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "RoleAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    TargetRoleId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    TargetRoleName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TargetUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    DetailsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleAuditLogs", x => x.Id);
                });

            // --- Data migration (research.md Decisions 7/2, data-model.md "Migration AddRoleManagement") ---
            // Must run here: after IsBuiltIn/RoleAuditLogs exist, before the unique index below
            // (which would fail on any surviving duplicate AspNetUserRoles row).

            // 1) Mark the two existing privileged roles built-in; create them if a fresh
            // database has neither yet. Idempotent — safe to run against an environment where
            // DevAdminSeeder or a prior deploy already created them.
            migrationBuilder.Sql("""
                UPDATE AspNetRoles
                SET IsBuiltIn = 1, ModifiedAtUtc = GETUTCDATE(), ModifiedBy = N'system:migration'
                WHERE NormalizedName IN (N'ADMINISTRATOR', N'SUPER USER');

                IF NOT EXISTS (SELECT 1 FROM AspNetRoles WHERE NormalizedName = N'ADMINISTRATOR')
                INSERT INTO AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp, IsBuiltIn, CreatedAtUtc, CreatedBy, ModifiedAtUtc, ModifiedBy)
                VALUES (CONVERT(nvarchar(450), NEWID()), N'Administrator', N'ADMINISTRATOR', CONVERT(nvarchar(max), NEWID()), 1, GETUTCDATE(), N'system:migration', GETUTCDATE(), N'system:migration');

                IF NOT EXISTS (SELECT 1 FROM AspNetRoles WHERE NormalizedName = N'SUPER USER')
                INSERT INTO AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp, IsBuiltIn, CreatedAtUtc, CreatedBy, ModifiedAtUtc, ModifiedBy)
                VALUES (CONVERT(nvarchar(450), NEWID()), N'Super User', N'SUPER USER', CONVERT(nvarchar(max), NEWID()), 1, GETUTCDATE(), N'system:migration', GETUTCDATE(), N'system:migration');
                """);

            // 2) De-duplicate: a user may hold only one role from now on (FR-012). For any user
            // with more than one AspNetUserRoles row, keep Super User > Administrator > lowest
            // RoleId, and record every removed assignment as a RoleAuditLog row (irreversible —
            // Down() does not restore these; see data-model.md and quickstart.md §0).
            migrationBuilder.Sql("""
                INSERT INTO RoleAuditLogs (Id, Action, ActorUserId, TargetRoleId, TargetRoleName, TargetUserId, DetailsJson, OccurredAtUtc, CreatedAtUtc, CreatedBy, RowVersion)
                SELECT
                    NEWID(),
                    5, -- RoleAuditAction.RoleRemoved
                    N'system:migration',
                    ranked.RoleId,
                    r.Name,
                    ranked.UserId,
                    N'{"reason":"duplicate-role-cleanup-during-AddRoleManagement-migration"}',
                    GETUTCDATE(),
                    GETUTCDATE(),
                    N'system:migration',
                    0x00
                FROM (
                    SELECT
                        ur.UserId,
                        ur.RoleId,
                        ROW_NUMBER() OVER (
                            PARTITION BY ur.UserId
                            ORDER BY
                                CASE WHEN r.NormalizedName = N'SUPER USER' THEN 0
                                     WHEN r.NormalizedName = N'ADMINISTRATOR' THEN 1
                                     ELSE 2 END,
                                ur.RoleId
                        ) AS RowNum
                    FROM AspNetUserRoles ur
                    INNER JOIN AspNetRoles r ON r.Id = ur.RoleId
                ) AS ranked
                INNER JOIN AspNetRoles r ON r.Id = ranked.RoleId
                WHERE ranked.RowNum > 1;

                DELETE ur
                FROM AspNetUserRoles ur
                INNER JOIN (
                    SELECT
                        ur2.UserId,
                        ur2.RoleId,
                        ROW_NUMBER() OVER (
                            PARTITION BY ur2.UserId
                            ORDER BY
                                CASE WHEN r2.NormalizedName = N'SUPER USER' THEN 0
                                     WHEN r2.NormalizedName = N'ADMINISTRATOR' THEN 1
                                     ELSE 2 END,
                                ur2.RoleId
                        ) AS RowNum
                    FROM AspNetUserRoles ur2
                    INNER JOIN AspNetRoles r2 ON r2.Id = ur2.RoleId
                ) AS ranked ON ranked.UserId = ur.UserId AND ranked.RoleId = ur.RoleId
                WHERE ranked.RowNum > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_UserId",
                table: "AspNetUserRoles",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId_ClaimType_ClaimValue",
                table: "AspNetRoleClaims",
                columns: new[] { "RoleId", "ClaimType", "ClaimValue" },
                unique: true,
                filter: "[ClaimType] IS NOT NULL AND [ClaimValue] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RoleAuditLogs_ActorUserId",
                table: "RoleAuditLogs",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RoleAuditLogs_OccurredAtUtc",
                table: "RoleAuditLogs",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RoleAuditLogs_TargetRoleId",
                table: "RoleAuditLogs",
                column: "TargetRoleId");

            migrationBuilder.CreateIndex(
                name: "IX_RoleAuditLogs_TargetUserId",
                table: "RoleAuditLogs",
                column: "TargetUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoleAuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUserRoles_UserId",
                table: "AspNetUserRoles");

            migrationBuilder.DropIndex(
                name: "IX_AspNetRoleClaims_RoleId_ClaimType_ClaimValue",
                table: "AspNetRoleClaims");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "AspNetRoles");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "AspNetRoles");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "AspNetRoles");

            migrationBuilder.DropColumn(
                name: "IsBuiltIn",
                table: "AspNetRoles");

            migrationBuilder.DropColumn(
                name: "ModifiedAtUtc",
                table: "AspNetRoles");

            migrationBuilder.DropColumn(
                name: "ModifiedBy",
                table: "AspNetRoles");

            migrationBuilder.AlterColumn<string>(
                name: "ClaimValue",
                table: "AspNetRoleClaims",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ClaimType",
                table: "AspNetRoleClaims",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");
        }
    }
}
