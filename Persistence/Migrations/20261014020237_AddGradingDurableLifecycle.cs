using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionProduccion.Grading.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGradingDurableLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceLeaseToken",
                table: "GradingResults",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<string>(
                name: "CancellationRequestedBy",
                table: "GradingJobs",
                type: "varchar(160)",
                maxLength: 160,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAtUtc",
                table: "GradingJobs",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ClaimVersion",
                table: "GradingJobs",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "LastDiagnosticCode",
                table: "GradingJobs",
                type: "varchar(160)",
                maxLength: 160,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "LeaseExpiresAtUtc",
                table: "GradingJobs",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LeaseToken",
                table: "GradingJobs",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateTable(
                name: "GradingAdmission",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradingAdmission", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "GradingAdmission",
                columns: new[] { "Id", "Version" },
                values: new object[] { 1, 1L });

            migrationBuilder.CreateIndex(
                name: "IX_GradingJobs_State_LeaseExpiresAtUtc",
                table: "GradingJobs",
                columns: new[] { "State", "LeaseExpiresAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GradingAdmission");

            migrationBuilder.DropIndex(
                name: "IX_GradingJobs_State_LeaseExpiresAtUtc",
                table: "GradingJobs");

            migrationBuilder.DropColumn(
                name: "SourceLeaseToken",
                table: "GradingResults");

            migrationBuilder.DropColumn(
                name: "CancellationRequestedBy",
                table: "GradingJobs");

            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                table: "GradingJobs");

            migrationBuilder.DropColumn(
                name: "ClaimVersion",
                table: "GradingJobs");

            migrationBuilder.DropColumn(
                name: "LastDiagnosticCode",
                table: "GradingJobs");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAtUtc",
                table: "GradingJobs");

            migrationBuilder.DropColumn(
                name: "LeaseToken",
                table: "GradingJobs");
        }
    }
}
