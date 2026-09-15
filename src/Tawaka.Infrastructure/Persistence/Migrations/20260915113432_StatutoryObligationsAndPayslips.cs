using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tawaka.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StatutoryObligationsAndPayslips : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Payslips",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayrollRunEmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayslipNumber = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    GeneratedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IsDevelopmentCopy = table.Column<bool>(type: "INTEGER", nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    IssuedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    SupersededByPayslipId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SupersedeReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    TemplateVersion = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payslips", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payslips_PayrollRunEmployees_PayrollRunEmployeeId",
                        column: x => x.PayrollRunEmployeeId,
                        principalTable: "PayrollRunEmployees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StatutoryObligations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayrollRunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayrollPeriodId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Authority = table.Column<int>(type: "INTEGER", nullable: false),
                    ObligationType = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    CalculatedAmount = table.Column<long>(type: "INTEGER", nullable: false),
                    IsCalculated = table.Column<bool>(type: "INTEGER", nullable: false),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    IsDeductionApplicable = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeductedAmount = table.Column<long>(type: "INTEGER", nullable: false),
                    IsDeducted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeductedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ApprovedAmount = table.Column<long>(type: "INTEGER", nullable: false),
                    IsApproved = table.Column<bool>(type: "INTEGER", nullable: false),
                    ApprovedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatutoryObligations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StatutoryObligationLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StatutoryObligationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayrollRunEmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeNumber = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    EmployeeName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    TaxNumber = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    NssaNumber = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatutoryObligationLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StatutoryObligationLines_StatutoryObligations_StatutoryObligationId",
                        column: x => x.StatutoryObligationId,
                        principalTable: "StatutoryObligations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StatutoryPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StatutoryObligationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    PaymentDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PaymentMethod = table.Column<int>(type: "INTEGER", nullable: false),
                    PaymentReference = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    AuthorityReceiptNumber = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    CompanyBankAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReceiptFilePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ReceiptFileHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    PenaltyOrInterestIncluded = table.Column<long>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    IsReversed = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReversalReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ReversedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ReversedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatutoryPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StatutoryPayments_StatutoryObligations_StatutoryObligationId",
                        column: x => x.StatutoryObligationId,
                        principalTable: "StatutoryObligations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Payslips_PayrollRunEmployeeId",
                table: "Payslips",
                column: "PayrollRunEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Payslips_PayslipNumber_Revision",
                table: "Payslips",
                columns: new[] { "PayslipNumber", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryObligationLines_StatutoryObligationId",
                table: "StatutoryObligationLines",
                column: "StatutoryObligationId");

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryObligations_CompanyId_DueDate",
                table: "StatutoryObligations",
                columns: new[] { "CompanyId", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryObligations_PayrollRunId_ObligationType_CurrencyCode",
                table: "StatutoryObligations",
                columns: new[] { "PayrollRunId", "ObligationType", "CurrencyCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryPayments_StatutoryObligationId_PaymentDate",
                table: "StatutoryPayments",
                columns: new[] { "StatutoryObligationId", "PaymentDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Payslips");

            migrationBuilder.DropTable(
                name: "StatutoryObligationLines");

            migrationBuilder.DropTable(
                name: "StatutoryPayments");

            migrationBuilder.DropTable(
                name: "StatutoryObligations");
        }
    }
}
