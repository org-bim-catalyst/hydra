using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RepairAspNetRolesConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Production incident, 2026-09-16: AspNetRoles had no PRIMARY KEY and no unique index
            // on NormalizedName at all (confirmed by the operator's own diagnostic queries against
            // the live database — a schema drift that predates every migration in this project and
            // was never caught because AskLucyDbContextModelSnapshot has always declared both
            // (HasKey("Id"), unique "RoleNameIndex" on NormalizedName) via ASP.NET Identity's own
            // conventions, so `dotnet ef migrations add` never sees a diff to generate here. The
            // missing PK let the "Super User" and "Administrator" rows silently share the exact
            // same Id value (ConcurrencyStamp on both was the literal string "1"/"2", not a GUID —
            // evidence they were inserted by hand, outside EF, not by 20260914180820_AddRoleManagement
            // whose INSERTs each generate their own NEWID()). Confirmed safe blast radius before
            // writing this: exactly one AspNetUserRoles row referenced the shared Id (already
            // repointed to the surviving "Super User" side by hand during triage) and zero
            // AspNetRoleClaims rows referenced it — so giving "Administrator" a fresh Id here
            // orphans nothing.
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1
                    FROM AspNetRoles su
                    INNER JOIN AspNetRoles adm ON adm.Id = su.Id AND adm.NormalizedName <> su.NormalizedName
                    WHERE su.NormalizedName = N'SUPER USER' AND adm.NormalizedName = N'ADMINISTRATOR'
                )
                BEGIN
                    UPDATE AspNetRoles
                    SET Id = CONVERT(nvarchar(450), NEWID()),
                        ConcurrencyStamp = CONVERT(nvarchar(max), NEWID()),
                        ModifiedAtUtc = GETUTCDATE(),
                        ModifiedBy = N'system:migration'
                    WHERE NormalizedName = N'ADMINISTRATOR';
                END;

                -- Normalizes the surviving row's ConcurrencyStamp too — "1"/"2" is not a GUID and
                -- is itself evidence of the same out-of-band insert that caused the duplicate Id.
                UPDATE AspNetRoles
                SET ConcurrencyStamp = CONVERT(nvarchar(max), NEWID())
                WHERE NormalizedName = N'SUPER USER' AND TRY_CONVERT(uniqueidentifier, ConcurrencyStamp) IS NULL;
                """);

            // Guarded rather than a plain AddPrimaryKey: only add it if the table truly has neither
            // a PK constraint nor any clustered index already occupying that slot (both checked,
            // since a clustered index under an unrelated name would otherwise collide). Safe to
            // run against a database that already has this repaired (e.g. re-running after a
            // partial deploy).
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[AspNetRoles]') AND index_id = 1)
                AND NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE type = 'PK' AND parent_object_id = OBJECT_ID(N'[dbo].[AspNetRoles]'))
                BEGIN
                    ALTER TABLE [AspNetRoles] ADD CONSTRAINT [PK_AspNetRoles] PRIMARY KEY CLUSTERED ([Id]);
                END;
                """);

            // Matches the exact index Identity's own conventions expect (see
            // AskLucyDbContextModelSnapshot.cs — HasIndex("NormalizedName").IsUnique()
            // .HasDatabaseName("RoleNameIndex")), which every InitialCreate-based database should
            // already have — guarded for the same reason as the PK above.
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'RoleNameIndex' AND object_id = OBJECT_ID(N'[dbo].[AspNetRoles]'))
                BEGIN
                    CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally a no-op (same precedent as 20260914180820_AddRoleManagement's own
            // irreversible data cleanup): reassigning "Administrator" back to a shared Id, or
            // dropping the PK/unique index this migration restores, would just reintroduce the
            // exact schema-integrity bug this migration exists to fix.
        }
    }
}
