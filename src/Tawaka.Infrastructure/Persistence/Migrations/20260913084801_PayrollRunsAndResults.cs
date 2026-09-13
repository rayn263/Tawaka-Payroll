using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tawaka.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PayrollRunsAndResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BracketApplication",
                table: "TaxRules",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "PayrollRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayrollPeriodId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RunNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    RunType = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Mode = table.Column<int>(type: "INTEGER", nullable: false),
                    CalculatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ReviewedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ApprovedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    FinalisedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    FinalisedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    PaidBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    PaidAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LockedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    LockedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ReopenedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ReopenedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ReopenReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    EngineVersion = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    RuleSnapshot = table.Column<string>(type: "TEXT", nullable: true),
                    ExchangeRateSnapshot = table.Column<string>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollRuns_PayrollPeriods_PayrollPeriodId",
                        column: x => x.PayrollPeriodId,
                        principalTable: "PayrollPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollRunEmployees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayrollRunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeContractId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContractVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    EmployeeNumber = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    EmployeeName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    EmploymentTypeCode = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    DepartmentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    GrossEarningsAmount = table.Column<long>(type: "INTEGER", nullable: true),
                    TaxableIncomeAmount = table.Column<long>(type: "INTEGER", nullable: true),
                    NssaInsurableEarningsAmount = table.Column<long>(type: "INTEGER", nullable: true),
                    NssaEmployeeAmount = table.Column<long>(type: "INTEGER", nullable: true),
                    PayeBeforeCreditsAmount = table.Column<long>(type: "INTEGER", nullable: true),
                    TaxCreditsAmount = table.Column<long>(type: "INTEGER", nullable: true),
                    PayeAfterCreditsAmount = table.Column<long>(type: "INTEGER", nullable: true),
                    AidsLevyAmount = table.Column<long>(type: "INTEGER", nullable: true),
                    TotalStatutoryDeductionsAmount = table.Column<long>(type: "INTEGER", nullable: true),
                    TotalOtherDeductionsAmount = table.Column<long>(type: "INTEGER", nullable: true),
                    TotalDeductionsAmount = table.Column<long>(type: "INTEGER", nullable: true),
                    NetPayAmount = table.Column<long>(type: "INTEGER", nullable: true),
                    NssaEmployerAmount = table.Column<long>(type: "INTEGER", nullable: true),
                    ApwcsAmount = table.Column<long>(type: "INTEGER", nullable: true),
                    TotalEmployerCostAmount = table.Column<long>(type: "INTEGER", nullable: true),
                    IsCalculated = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsExcluded = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExclusionReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollRunEmployees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollRunEmployees_PayrollRuns_PayrollRunId",
                        column: x => x.PayrollRunId,
                        principalTable: "PayrollRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PayrollCalculationTraces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayrollRunEmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    Stage = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    ItemKey = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    RuleId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    RuleType = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    VerificationStatus = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    RuleSource = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    RuleEffectiveFrom = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Inputs = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Steps = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    RawValue = table.Column<long>(type: "INTEGER", nullable: true),
                    OutputAmount = table.Column<long>(type: "INTEGER", nullable: true),
                    OutputCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: true),
                    RoundingApplied = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    Explanation = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Conversion = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollCalculationTraces", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollCalculationTraces_PayrollRunEmployees_PayrollRunEmployeeId",
                        column: x => x.PayrollRunEmployeeId,
                        principalTable: "PayrollRunEmployees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PayrollCostAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayrollRunEmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProjectName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    DepartmentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DepartmentName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    Percent = table.Column<long>(type: "INTEGER", nullable: false),
                    AllocatedCostAmount = table.Column<long>(type: "INTEGER", nullable: false),
                    CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollCostAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollCostAllocations_PayrollRunEmployees_PayrollRunEmployeeId",
                        column: x => x.PayrollRunEmployeeId,
                        principalTable: "PayrollRunEmployees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PayrollDeductionLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayrollRunEmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    IsStatutory = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReducesTaxableIncome = table.Column<bool>(type: "INTEGER", nullable: false),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollDeductionLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollDeductionLines_PayrollRunEmployees_PayrollRunEmployeeId",
                        column: x => x.PayrollRunEmployeeId,
                        principalTable: "PayrollRunEmployees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PayrollEarningLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayrollRunEmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    Quantity = table.Column<long>(type: "INTEGER", nullable: true),
                    RateAmount = table.Column<long>(type: "INTEGER", nullable: true),
                    TaxableAmount = table.Column<long>(type: "INTEGER", nullable: false),
                    ExemptAmount = table.Column<long>(type: "INTEGER", nullable: false),
                    NssaApplicableAmount = table.Column<long>(type: "INTEGER", nullable: false),
                    IsIncludedInGross = table.Column<bool>(type: "INTEGER", nullable: false),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    OriginalAmount = table.Column<long>(type: "INTEGER", nullable: true),
                    OriginalCurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: true),
                    ExchangeRateUsed = table.Column<long>(type: "INTEGER", nullable: true),
                    ExchangeRateDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ExchangeRateSource = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollEarningLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollEarningLines_PayrollRunEmployees_PayrollRunEmployeeId",
                        column: x => x.PayrollRunEmployeeId,
                        principalTable: "PayrollRunEmployees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PayrollEmployerCostLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayrollRunEmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    BaseAmount = table.Column<long>(type: "INTEGER", nullable: false),
                    RateApplied = table.Column<long>(type: "INTEGER", nullable: true),
                    RuleId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollEmployerCostLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollEmployerCostLines_PayrollRunEmployees_PayrollRunEmployeeId",
                        column: x => x.PayrollRunEmployeeId,
                        principalTable: "PayrollRunEmployees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PayrollUnresolvedItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayrollRunEmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    ItemKey = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    RuleType = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    RuleId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    VerificationStatus = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    ComplianceQuestion = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    Remedy = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollUnresolvedItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollUnresolvedItems_PayrollRunEmployees_PayrollRunEmployeeId",
                        column: x => x.PayrollRunEmployeeId,
                        principalTable: "PayrollRunEmployees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollCalculationTraces_PayrollRunEmployeeId_Sequence",
                table: "PayrollCalculationTraces",
                columns: new[] { "PayrollRunEmployeeId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollCostAllocations_PayrollRunEmployeeId",
                table: "PayrollCostAllocations",
                column: "PayrollRunEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollCostAllocations_ProjectId",
                table: "PayrollCostAllocations",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollDeductionLines_PayrollRunEmployeeId",
                table: "PayrollDeductionLines",
                column: "PayrollRunEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollEarningLines_PayrollRunEmployeeId",
                table: "PayrollEarningLines",
                column: "PayrollRunEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollEmployerCostLines_PayrollRunEmployeeId",
                table: "PayrollEmployerCostLines",
                column: "PayrollRunEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRunEmployees_PayrollRunId_EmployeeId",
                table: "PayrollRunEmployees",
                columns: new[] { "PayrollRunId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_PayrollPeriodId_RunNumber",
                table: "PayrollRuns",
                columns: new[] { "PayrollPeriodId", "RunNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_Status",
                table: "PayrollRuns",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollUnresolvedItems_PayrollRunEmployeeId",
                table: "PayrollUnresolvedItems",
                column: "PayrollRunEmployeeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayrollCalculationTraces");

            migrationBuilder.DropTable(
                name: "PayrollCostAllocations");

            migrationBuilder.DropTable(
                name: "PayrollDeductionLines");

            migrationBuilder.DropTable(
                name: "PayrollEarningLines");

            migrationBuilder.DropTable(
                name: "PayrollEmployerCostLines");

            migrationBuilder.DropTable(
                name: "PayrollUnresolvedItems");

            migrationBuilder.DropTable(
                name: "PayrollRunEmployees");

            migrationBuilder.DropTable(
                name: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "BracketApplication",
                table: "TaxRules");
        }
    }
}
