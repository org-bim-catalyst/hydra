using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AskLucy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderFailureClassificationAndOptionalModelLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // specs/043: additive columns plus one widening. No backfill is needed on the way
            // up - no AIModels row can hold 0, because the pre-feature AIModel.Create rejected
            // it and that factory is the only construction path. Nothing is dropped and no
            // column stops being read, so no two-step deploy is required: a running old build
            // neither writes nor reads a NULL here.
            migrationBuilder.AddColumn<string>(
                name: "FailureKind",
                table: "ProviderHealthChecks",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FailureReason",
                table: "ProviderHealthChecks",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HealthFailureKind",
                table: "AIProviders",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HealthFailureReason",
                table: "AIProviders",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "MaxOutputTokens",
                table: "AIModels",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "ContextWindowTokens",
                table: "AIModels",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FailureKind",
                table: "ProviderHealthChecks");

            migrationBuilder.DropColumn(
                name: "FailureReason",
                table: "ProviderHealthChecks");

            migrationBuilder.DropColumn(
                name: "HealthFailureKind",
                table: "AIProviders");

            migrationBuilder.DropColumn(
                name: "HealthFailureReason",
                table: "AIProviders");

            // specs/043 data-model.md: backfill before narrowing, or the NOT NULL alter fails on
            // any row added since this migration shipped. The round-trip is lossy by nature -
            // a model whose vendor published no token limits comes back as 0, which the
            // pre-feature domain rule would itself have rejected. That is the cost of reversing
            // this migration, not a defect in it.
            migrationBuilder.Sql("UPDATE [AIModels] SET [ContextWindowTokens] = 0 WHERE [ContextWindowTokens] IS NULL;");
            migrationBuilder.Sql("UPDATE [AIModels] SET [MaxOutputTokens] = 0 WHERE [MaxOutputTokens] IS NULL;");

            migrationBuilder.AlterColumn<int>(
                name: "MaxOutputTokens",
                table: "AIModels",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ContextWindowTokens",
                table: "AIModels",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}
