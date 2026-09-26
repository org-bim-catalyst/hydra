using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RepairIdentityClaimsTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // specs/074, found 2026-09-26: on both the production and the shared test database,
            // AspNetRoleClaims and AspNetUserClaims have no IDENTITY on Id, no primary key and no
            // foreign key — the same out-of-band schema drift 20260916080502_RepairAspNetRolesConstraints
            // repaired on AspNetRoles, invisible to `dotnet ef migrations add` because the snapshot
            // has always declared all three. Every role-claim insert therefore failed with "Cannot
            // insert the value NULL into column 'Id'": no custom role with a permission could ever be
            // created, and the Administrator content-access switch (FR-016g) could never be stored.
            // Both tables were empty on both databases when this was written; rebuilding the Id
            // column assigns fresh values to any rows that exist by then, which nothing references.
            // Each step is guarded so the migration is a no-op on a database created by InitialCreate.
            RepairClaimsTable(migrationBuilder, "AspNetRoleClaims", "RoleId", "AspNetRoles", indexOwnerColumn: false);
            RepairClaimsTable(migrationBuilder, "AspNetUserClaims", "UserId", "AspNetUsers", indexOwnerColumn: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally a no-op (same precedent as 20260916080502_RepairAspNetRolesConstraints):
            // removing the identity, key and foreign key would just reintroduce the drift.
        }

        // AspNetRoleClaims' single-column RoleId index was deliberately replaced by the composite
        // IX_AspNetRoleClaims_RoleId_ClaimType_ClaimValue in 20260914180820_AddRoleManagement, so
        // only AspNetUserClaims gets its owner-column index back.
        private static void RepairClaimsTable(
            MigrationBuilder migrationBuilder, string table, string ownerColumn, string principalTable, bool indexOwnerColumn)
        {
            migrationBuilder.Sql($"""
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[{table}]') AND name = N'Id' AND is_identity = 1)
                AND NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE type = 'PK' AND parent_object_id = OBJECT_ID(N'[dbo].[{table}]'))
                BEGIN
                    ALTER TABLE [{table}] DROP COLUMN [Id];
                    ALTER TABLE [{table}] ADD [Id] int IDENTITY(1, 1) NOT NULL;
                END;
                """);

            migrationBuilder.Sql($"""
                IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE type = 'PK' AND parent_object_id = OBJECT_ID(N'[dbo].[{table}]'))
                BEGIN
                    ALTER TABLE [{table}] ADD CONSTRAINT [PK_{table}] PRIMARY KEY CLUSTERED ([Id]);
                END;
                """);

            migrationBuilder.Sql($"""
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_{table}_{principalTable}_{ownerColumn}')
                BEGIN
                    ALTER TABLE [{table}] ADD CONSTRAINT [FK_{table}_{principalTable}_{ownerColumn}]
                        FOREIGN KEY ([{ownerColumn}]) REFERENCES [{principalTable}] ([Id]) ON DELETE CASCADE;
                END;
                """);

            if (indexOwnerColumn)
            {
                migrationBuilder.Sql($"""
                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{table}_{ownerColumn}' AND object_id = OBJECT_ID(N'[dbo].[{table}]'))
                    BEGIN
                        CREATE INDEX [IX_{table}_{ownerColumn}] ON [{table}] ([{ownerColumn}]);
                    END;
                    """);
            }
        }
    }
}
