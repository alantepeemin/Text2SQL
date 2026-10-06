using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Text2Sql.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SaaS2_Membership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "RefreshTokens",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Memberships",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    InvitationType = table.Column<string>(type: "text", nullable: true),
                    InvitedBy = table.Column<int>(type: "integer", nullable: true),
                    InvitedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedBy = table.Column<int>(type: "integer", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovalNotes = table.Column<string>(type: "text", nullable: true),
                    DeactivatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeactivatedBy = table.Column<int>(type: "integer", nullable: true),
                    JoinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Memberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Memberships_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Memberships_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_CompanyId",
                table: "Memberships",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_UserId_CompanyId",
                table: "Memberships",
                columns: new[] { "UserId", "CompanyId" },
                unique: true);
            
            migrationBuilder.Sql(@"
                INSERT INTO ""Memberships""
                    (""UserId"",""CompanyId"",""Role"",""Status"",""IsActive"",""IsPrimary"",
                    ""InvitationType"",""InvitedBy"",""InvitedAt"",""ApprovedBy"",""ApprovedAt"",
                    ""ApprovalNotes"",""DeactivatedAt"",""DeactivatedBy"",""JoinedAt"",""UpdatedAt"")
                SELECT u.""Id"", u.""CompanyId"", u.""Role"", u.""Status"", u.""IsActive"", TRUE,
                    u.""InvitationType"", u.""InvitedBy"", u.""InvitedAt"", u.""ApprovedBy"", u.""ApprovedAt"",
                    u.""ApprovalNotes"", u.""DeactivatedAt"", u.""DeactivatedBy"", u.""CreatedAt"", NOW()
                FROM ""Users"" u
                WHERE NOT EXISTS (
                    SELECT 1 FROM ""Memberships"" m
                    WHERE m.""UserId"" = u.""Id"" AND m.""CompanyId"" = u.""CompanyId"");");

            // Mevcut refresh token'lara organizasyon bağlamı ver
            migrationBuilder.Sql(@"
                UPDATE ""RefreshTokens"" r SET ""CompanyId"" = u.""CompanyId""
                FROM ""Users"" u WHERE r.""UserId"" = u.""Id"" AND r.""CompanyId"" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Memberships");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "RefreshTokens");
        }
    }
}
