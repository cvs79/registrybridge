using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RegistryBridge.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSynchronizationRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "synchronization_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CatalogRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Origin = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_synchronization_runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_synchronization_runs_catalog_revisions_CatalogRevisionId",
                        column: x => x.CatalogRevisionId,
                        principalTable: "catalog_revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "artifact_outcomes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SynchronizationRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    CatalogEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Disposition = table.Column<string>(type: "text", nullable: false),
                    Detail = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_artifact_outcomes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_artifact_outcomes_synchronization_runs_SynchronizationRunId",
                        column: x => x.SynchronizationRunId,
                        principalTable: "synchronization_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "run_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SynchronizationRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EventType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Data = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_run_logs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_run_logs_synchronization_runs_SynchronizationRunId",
                        column: x => x.SynchronizationRunId,
                        principalTable: "synchronization_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_artifact_outcomes_SynchronizationRunId_Order",
                table: "artifact_outcomes",
                columns: new[] { "SynchronizationRunId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_run_logs_SynchronizationRunId_OccurredAt",
                table: "run_logs",
                columns: new[] { "SynchronizationRunId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_synchronization_runs_CatalogRevisionId",
                table: "synchronization_runs",
                column: "CatalogRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_synchronization_runs_Status_CreatedAt",
                table: "synchronization_runs",
                columns: new[] { "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "artifact_outcomes");

            migrationBuilder.DropTable(
                name: "run_logs");

            migrationBuilder.DropTable(
                name: "synchronization_runs");
        }
    }
}
