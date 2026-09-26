using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDefaultUserRoleAndRepairIdentityJoinTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Found 2026-09-26, the same out-of-band drift 20260926113107_RepairIdentityClaimsTables
            // repaired on the claims tables: on both the production and the shared test database,
            // AspNetUserLogins, AspNetUserRoles and AspNetUserTokens have no primary key and no foreign
            // key, so deleting a user or a role left rows pointing at nothing (the shared test database
            // had hundreds). Orphans are removed first - they reference rows that no longer exist, so
            // nothing can use them - then the keys InitialCreate declares are put back. Each step is
            // guarded so this is a no-op on a database created by InitialCreate.
            migrationBuilder.Sql("""
                DELETE l FROM AspNetUserLogins l WHERE NOT EXISTS (SELECT 1 FROM AspNetUsers u WHERE u.Id = l.UserId);
                DELETE t FROM AspNetUserTokens t WHERE NOT EXISTS (SELECT 1 FROM AspNetUsers u WHERE u.Id = t.UserId);
                DELETE ur FROM AspNetUserRoles ur WHERE NOT EXISTS (SELECT 1 FROM AspNetUsers u WHERE u.Id = ur.UserId);
                DELETE ur FROM AspNetUserRoles ur WHERE NOT EXISTS (SELECT 1 FROM AspNetRoles r WHERE r.Id = ur.RoleId);
                """);

            AddPrimaryKey(migrationBuilder, "AspNetUserLogins", "[LoginProvider], [ProviderKey]");
            AddPrimaryKey(migrationBuilder, "AspNetUserRoles", "[UserId], [RoleId]");
            AddPrimaryKey(migrationBuilder, "AspNetUserTokens", "[UserId], [LoginProvider], [Name]");

            AddForeignKey(migrationBuilder, "AspNetUserLogins", "UserId", "AspNetUsers");
            AddForeignKey(migrationBuilder, "AspNetUserRoles", "RoleId", "AspNetRoles");
            AddForeignKey(migrationBuilder, "AspNetUserRoles", "UserId", "AspNetUsers");
            AddForeignKey(migrationBuilder, "AspNetUserTokens", "UserId", "AspNetUsers");

            AddIndex(migrationBuilder, "AspNetUserLogins", "UserId");
            AddIndex(migrationBuilder, "AspNetUserRoles", "RoleId");

            // 2) The built-in User role every account holds unless it's given another one
            // (Application/Authorization/DefaultRole.cs). "User" was never reserved before, so a custom
            // role may already carry the name: it keeps its holders and permissions under a new name.
            migrationBuilder.Sql("""
                UPDATE AspNetRoles
                SET Name = N'User (custom ' + LEFT(Id, 8) + N')',
                    NormalizedName = N'USER (CUSTOM ' + UPPER(LEFT(Id, 8)) + N')',
                    ModifiedAtUtc = GETUTCDATE(),
                    ModifiedBy = N'system:migration'
                WHERE NormalizedName = N'USER' AND IsBuiltIn = 0;

                IF NOT EXISTS (SELECT 1 FROM AspNetRoles WHERE NormalizedName = N'USER')
                INSERT INTO AspNetRoles (Id, Name, NormalizedName, Description, ConcurrencyStamp, IsBuiltIn, CreatedAtUtc, CreatedBy, ModifiedAtUtc, ModifiedBy)
                VALUES (CONVERT(nvarchar(450), NEWID()), N'User', N'USER', N'Every account''s starting role: signs in and uses the site, with no administration access.',
                        CONVERT(nvarchar(max), NEWID()), 1, GETUTCDATE(), N'system:migration', GETUTCDATE(), N'system:migration');
                """);

            // 3) No account is ever left with no role: every account without one moves to User, with a
            // RoleAssigned audit row each (irreversible - Down() does not undo it).
            migrationBuilder.Sql("""
                DECLARE @userRoleId nvarchar(450) = (SELECT Id FROM AspNetRoles WHERE NormalizedName = N'USER');

                INSERT INTO RoleAuditLogs (Id, Action, ActorUserId, TargetRoleId, TargetRoleName, TargetUserId, DetailsJson, OccurredAtUtc, CreatedAtUtc, CreatedBy, RowVersion)
                SELECT
                    NEWID(),
                    3, -- RoleAuditAction.RoleAssigned
                    N'system:migration',
                    @userRoleId,
                    N'User',
                    u.Id,
                    N'{"reason":"default-role-backfill-during-AddDefaultUserRole-migration"}',
                    GETUTCDATE(),
                    GETUTCDATE(),
                    N'system:migration',
                    0x00
                FROM AspNetUsers u
                WHERE NOT EXISTS (SELECT 1 FROM AspNetUserRoles ur WHERE ur.UserId = u.Id);

                INSERT INTO AspNetUserRoles (UserId, RoleId)
                SELECT u.Id, @userRoleId
                FROM AspNetUsers u
                WHERE NOT EXISTS (SELECT 1 FROM AspNetUserRoles ur WHERE ur.UserId = u.Id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally a no-op (same precedent as 20260926113107_RepairIdentityClaimsTables):
            // removing the keys would reintroduce the drift, and removing the User role would leave
            // every account that holds it with no role - the state this migration exists to end.
        }

        private static void AddPrimaryKey(MigrationBuilder migrationBuilder, string table, string columns) =>
            migrationBuilder.Sql($"""
                IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE type = 'PK' AND parent_object_id = OBJECT_ID(N'[dbo].[{table}]'))
                BEGIN
                    ALTER TABLE [{table}] ADD CONSTRAINT [PK_{table}] PRIMARY KEY CLUSTERED ({columns});
                END;
                """);

        private static void AddForeignKey(MigrationBuilder migrationBuilder, string table, string column, string principalTable) =>
            migrationBuilder.Sql($"""
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_{table}_{principalTable}_{column}')
                BEGIN
                    ALTER TABLE [{table}] ADD CONSTRAINT [FK_{table}_{principalTable}_{column}]
                        FOREIGN KEY ([{column}]) REFERENCES [{principalTable}] ([Id]) ON DELETE CASCADE;
                END;
                """);

        private static void AddIndex(MigrationBuilder migrationBuilder, string table, string column) =>
            migrationBuilder.Sql($"""
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{table}_{column}' AND object_id = OBJECT_ID(N'[dbo].[{table}]'))
                BEGIN
                    CREATE INDEX [IX_{table}_{column}] ON [{table}] ([{column}]);
                END;
                """);
    }
}
