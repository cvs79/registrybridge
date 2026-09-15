using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RegistryBridge.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRunLogTruncation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "LogsTruncated",
                table: "synchronization_runs",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LogsTruncated",
                table: "synchronization_runs");
        }
    }
}
