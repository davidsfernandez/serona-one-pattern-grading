using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionProduccion.Grading.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddModelingGradingPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GradingApprovals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ModelId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ResultId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ApprovedBy = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradingApprovals", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GradingJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ModelId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    RevisionId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CompanyId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_bin")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OwnerId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_bin")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    State = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CancellationRequestedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradingJobs", x => x.Id);
                    table.UniqueConstraint("AK_GradingJobs_Id_RevisionId_ModelId", x => new { x.Id, x.RevisionId, x.ModelId });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GradingResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ModelId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    RevisionId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    JobId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    IsValid = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SourceJobVersion = table.Column<long>(type: "bigint", nullable: false),
                    ArtifactKey = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ArtifactHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ArtifactLength = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradingResults", x => x.Id);
                    table.UniqueConstraint("AK_GradingResults_Id_ModelId", x => new { x.Id, x.ModelId });
                    table.ForeignKey(
                        name: "FK_GradingResults_GradingJobs_JobId_RevisionId_ModelId",
                        columns: x => new { x.JobId, x.RevisionId, x.ModelId },
                        principalTable: "GradingJobs",
                        principalColumns: new[] { "Id", "RevisionId", "ModelId" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ModelingDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CompanyId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_bin")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OwnerId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_bin")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsArchived = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CurrentApprovedResultId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    OriginalKey = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OriginalHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OriginalLength = table.Column<long>(type: "bigint", nullable: false),
                    FixedGeometryHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelingDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModelingDocuments_GradingResults_CurrentApprovedResultId_Id",
                        columns: x => new { x.CurrentApprovedResultId, x.Id },
                        principalTable: "GradingResults",
                        principalColumns: new[] { "Id", "ModelId" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ModelingAudit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ModelId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Action = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ActorId = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ResourceId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelingAudit", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModelingAudit_ModelingDocuments_ModelId",
                        column: x => x.ModelId,
                        principalTable: "ModelingDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ModelingDrafts",
                columns: table => new
                {
                    ModelId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    ArtifactKey = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ArtifactHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ArtifactLength = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedBy = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelingDrafts", x => x.ModelId);
                    table.ForeignKey(
                        name: "FK_ModelingDrafts_ModelingDocuments_ModelId",
                        column: x => x.ModelId,
                        principalTable: "ModelingDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ModelingRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ModelId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Number = table.Column<int>(type: "int", nullable: false),
                    DraftVersion = table.Column<long>(type: "bigint", nullable: false),
                    ArtifactKey = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ArtifactHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ArtifactLength = table.Column<long>(type: "bigint", nullable: false),
                    CreatedBy = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelingRevisions", x => x.Id);
                    table.UniqueConstraint("AK_ModelingRevisions_Id_ModelId", x => new { x.Id, x.ModelId });
                    table.ForeignKey(
                        name: "FK_ModelingRevisions_ModelingDocuments_ModelId",
                        column: x => x.ModelId,
                        principalTable: "ModelingDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ModelingOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CompanyId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_bin")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OwnerId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_bin")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OperationId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Kind = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Fingerprint = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ModelId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    RevisionId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    JobId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ApprovalId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelingOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModelingOperations_ModelingRevisions_RevisionId_ModelId",
                        columns: x => new { x.RevisionId, x.ModelId },
                        principalTable: "ModelingRevisions",
                        principalColumns: new[] { "Id", "ModelId" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_GradingApprovals_ResultId_ModelId",
                table: "GradingApprovals",
                columns: new[] { "ResultId", "ModelId" });

            migrationBuilder.CreateIndex(
                name: "IX_GradingJobs_CompanyId_OwnerId_State",
                table: "GradingJobs",
                columns: new[] { "CompanyId", "OwnerId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_GradingJobs_ModelId_State",
                table: "GradingJobs",
                columns: new[] { "ModelId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_GradingJobs_RevisionId_ModelId",
                table: "GradingJobs",
                columns: new[] { "RevisionId", "ModelId" });

            migrationBuilder.CreateIndex(
                name: "IX_GradingResults_JobId",
                table: "GradingResults",
                column: "JobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GradingResults_JobId_RevisionId_ModelId",
                table: "GradingResults",
                columns: new[] { "JobId", "RevisionId", "ModelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModelingAudit_ModelId",
                table: "ModelingAudit",
                column: "ModelId");

            migrationBuilder.CreateIndex(
                name: "IX_ModelingDocuments_CompanyId_OwnerId_IsArchived",
                table: "ModelingDocuments",
                columns: new[] { "CompanyId", "OwnerId", "IsArchived" });

            migrationBuilder.CreateIndex(
                name: "IX_ModelingDocuments_CurrentApprovedResultId_Id",
                table: "ModelingDocuments",
                columns: new[] { "CurrentApprovedResultId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ModelingOperations_CompanyId_OwnerId_OperationId",
                table: "ModelingOperations",
                columns: new[] { "CompanyId", "OwnerId", "OperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModelingOperations_RevisionId_ModelId",
                table: "ModelingOperations",
                columns: new[] { "RevisionId", "ModelId" });

            migrationBuilder.CreateIndex(
                name: "IX_ModelingRevisions_ModelId_Number",
                table: "ModelingRevisions",
                columns: new[] { "ModelId", "Number" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_GradingApprovals_GradingResults_ResultId_ModelId",
                table: "GradingApprovals",
                columns: new[] { "ResultId", "ModelId" },
                principalTable: "GradingResults",
                principalColumns: new[] { "Id", "ModelId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GradingJobs_ModelingRevisions_RevisionId_ModelId",
                table: "GradingJobs",
                columns: new[] { "RevisionId", "ModelId" },
                principalTable: "ModelingRevisions",
                principalColumns: new[] { "Id", "ModelId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ModelingDocuments_GradingResults_CurrentApprovedResultId_Id",
                table: "ModelingDocuments");

            migrationBuilder.DropTable(
                name: "GradingApprovals");

            migrationBuilder.DropTable(
                name: "ModelingAudit");

            migrationBuilder.DropTable(
                name: "ModelingDrafts");

            migrationBuilder.DropTable(
                name: "ModelingOperations");

            migrationBuilder.DropTable(
                name: "GradingResults");

            migrationBuilder.DropTable(
                name: "GradingJobs");

            migrationBuilder.DropTable(
                name: "ModelingRevisions");

            migrationBuilder.DropTable(
                name: "ModelingDocuments");
        }
    }
}
