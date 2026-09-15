using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tawaka.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccountingAuditAndSiteCost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProjectSiteId",
                table: "PayrollCostAllocations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProjectSiteName",
                table: "PayrollCostAllocations",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GlAccountMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MappingType = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    AccountCode = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    AccountName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CostCentre = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlAccountMappings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollCostAllocations_ProjectSiteId",
                table: "PayrollCostAllocations",
                column: "ProjectSiteId");

            migrationBuilder.CreateIndex(
                name: "IX_GlAccountMappings_CompanyId_MappingType_CurrencyCode",
                table: "GlAccountMappings",
                columns: new[] { "CompanyId", "MappingType", "CurrencyCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GlAccountMappings");

            migrationBuilder.DropIndex(
                name: "IX_PayrollCostAllocations_ProjectSiteId",
                table: "PayrollCostAllocations");

            migrationBuilder.DropColumn(
                name: "ProjectSiteId",
                table: "PayrollCostAllocations");

            migrationBuilder.DropColumn(
                name: "ProjectSiteName",
                table: "PayrollCostAllocations");
        }
    }
}
