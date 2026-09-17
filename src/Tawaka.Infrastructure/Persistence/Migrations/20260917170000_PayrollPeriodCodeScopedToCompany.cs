using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tawaka.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Scopes the payroll period code to its company.
    /// <para>
    /// The code was unique across the whole database, so two companies sharing one database could
    /// not both have a period called "2026-09". Every other company-owned code in the schema —
    /// employee number, project code, department code, loan number — is unique within its company,
    /// and this one was the exception.
    /// </para>
    /// </summary>
    public partial class PayrollPeriodCodeScopedToCompany : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PayrollPeriods_Code",
                table: "PayrollPeriods");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollPeriods_CompanyId_Code",
                table: "PayrollPeriods",
                columns: new[] { "CompanyId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PayrollPeriods_CompanyId_Code",
                table: "PayrollPeriods");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollPeriods_Code",
                table: "PayrollPeriods",
                column: "Code",
                unique: true);
        }
    }
}
