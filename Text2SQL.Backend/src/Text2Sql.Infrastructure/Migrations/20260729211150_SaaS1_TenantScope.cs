using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Text2Sql.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SaaS1_TenantScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "QueryHistories",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "ProjectAccesses",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_QueryHistories_CompanyId_CreatedAt",
                table: "QueryHistories",
                columns: new[] { "CompanyId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAccesses_CompanyId",
                table: "ProjectAccesses",
                column: "CompanyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QueryHistories_CompanyId_CreatedAt",
                table: "QueryHistories");

            migrationBuilder.DropIndex(
                name: "IX_ProjectAccesses_CompanyId",
                table: "ProjectAccesses");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "QueryHistories");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "ProjectAccesses");
        }
    }
}
