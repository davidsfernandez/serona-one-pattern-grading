using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionProduccion.Grading.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGradingNestingHandoffs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GradingNestingHandoffs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    OperationId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ModelId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    RevisionId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ResultId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ApprovalId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CompanyId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_bin")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OwnerId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_bin")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Fingerprint = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ArtifactKey = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ArtifactHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ArtifactLength = table.Column<long>(type: "bigint", nullable: false),
                    GeometryHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradingNestingHandoffs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GradingNestingHandoffs_GradingApprovals_ApprovalId",
                        column: x => x.ApprovalId,
                        principalTable: "GradingApprovals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GradingNestingHandoffs_GradingResults_ResultId_ModelId",
                        columns: x => new { x.ResultId, x.ModelId },
                        principalTable: "GradingResults",
                        principalColumns: new[] { "Id", "ModelId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GradingNestingHandoffs_ModelingDocuments_ModelId",
                        column: x => x.ModelId,
                        principalTable: "ModelingDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GradingNestingHandoffs_ModelingRevisions_RevisionId_ModelId",
                        columns: x => new { x.RevisionId, x.ModelId },
                        principalTable: "ModelingRevisions",
                        principalColumns: new[] { "Id", "ModelId" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_GradingNestingHandoffs_ApprovalId",
                table: "GradingNestingHandoffs",
                column: "ApprovalId");

            migrationBuilder.CreateIndex(
                name: "IX_GradingNestingHandoffs_CompanyId_OwnerId_OperationId",
                table: "GradingNestingHandoffs",
                columns: new[] { "CompanyId", "OwnerId", "OperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GradingNestingHandoffs_ModelId",
                table: "GradingNestingHandoffs",
                column: "ModelId");

            migrationBuilder.CreateIndex(
                name: "IX_GradingNestingHandoffs_ResultId_ModelId",
                table: "GradingNestingHandoffs",
                columns: new[] { "ResultId", "ModelId" });

            migrationBuilder.CreateIndex(
                name: "IX_GradingNestingHandoffs_RevisionId_ModelId",
                table: "GradingNestingHandoffs",
                columns: new[] { "RevisionId", "ModelId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GradingNestingHandoffs");
        }
    }
}
