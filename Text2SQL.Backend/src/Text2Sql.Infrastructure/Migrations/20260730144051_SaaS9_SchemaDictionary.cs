using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Text2Sql.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SaaS9_SchemaDictionary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QueryFeedbacks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    QueryHistoryId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    IsHelpful = table.Column<bool>(type: "boolean", nullable: false),
                    CorrectedSql = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueryFeedbacks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QueryFeedbacks_QueryHistories_QueryHistoryId",
                        column: x => x.QueryHistoryId,
                        principalTable: "QueryHistories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SchemaAnnotations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    DataSourceId = table.Column<int>(type: "integer", nullable: false),
                    TableName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ColumnName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchemaAnnotations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SchemaAnnotations_ProjectDatabases_DataSourceId",
                        column: x => x.DataSourceId,
                        principalTable: "ProjectDatabases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QueryFeedbacks_QueryHistoryId_UserId",
                table: "QueryFeedbacks",
                columns: new[] { "QueryHistoryId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SchemaAnnotations_DataSourceId_TableName_ColumnName",
                table: "SchemaAnnotations",
                columns: new[] { "DataSourceId", "TableName", "ColumnName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QueryFeedbacks");

            migrationBuilder.DropTable(
                name: "SchemaAnnotations");
        }
    }
}
