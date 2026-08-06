using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RegistryBridge.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "catalog_state",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    CurrentRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalog_state", x => x.Id);
                    table.ForeignKey(
                        name: "FK_catalog_state_catalog_revisions_CurrentRevisionId",
                        column: x => x.CurrentRevisionId,
                        principalTable: "catalog_revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "catalog_state",
                columns: new[] { "Id", "CurrentRevisionId" },
                values: new object[] { 1, null });

            migrationBuilder.CreateIndex(
                name: "IX_catalog_state_CurrentRevisionId",
                table: "catalog_state",
                column: "CurrentRevisionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "catalog_state");
        }
    }
}
