using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tawaka.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TimeLeaveCalendarAndLoans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmployeeLoans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LoanNumber = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    PrincipalAmount = table.Column<long>(type: "INTEGER", nullable: false),
                    DisbursementDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    DisbursementReference = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    InterestRatePercent = table.Column<long>(type: "INTEGER", nullable: true),
                    InterestAmount = table.Column<long>(type: "INTEGER", nullable: false),
                    InstalmentCount = table.Column<int>(type: "INTEGER", nullable: false),
                    InstalmentAmount = table.Column<long>(type: "INTEGER", nullable: false),
                    FirstInstalmentDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ApprovalStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    SubmittedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ApprovedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    DecisionReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    AllowsOverRecovery = table.Column<bool>(type: "INTEGER", nullable: false),
                    OverRecoveryApprovalReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    OverRecoveryApprovedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Purpose = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeLoans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HolidayCalendars",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HolidayCalendars", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LeaveTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    IsPaid = table.Column<bool>(type: "INTEGER", nullable: false),
                    AccrualBasis = table.Column<int>(type: "INTEGER", nullable: false),
                    EntitlementDays = table.Column<long>(type: "INTEGER", nullable: true),
                    StatutoryEntitlementDays = table.Column<long>(type: "INTEGER", nullable: true),
                    EntitlementVerificationStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    EntitlementSource = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ComplianceQuestion = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    RequiresApproval = table.Column<bool>(type: "INTEGER", nullable: false),
                    CarriesForward = table.Column<bool>(type: "INTEGER", nullable: false),
                    MaximumCarryForwardDays = table.Column<long>(type: "INTEGER", nullable: true),
                    IncludesNonWorkingDays = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OvertimeRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CategoryCode = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CategoryName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Multiplier = table.Column<long>(type: "INTEGER", nullable: true),
                    ThresholdHours = table.Column<long>(type: "INTEGER", nullable: true),
                    IsTaxable = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsNssaApplicable = table.Column<bool>(type: "INTEGER", nullable: false),
                    EmploymentTypeCode = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OvertimeRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OvertimeRules_StatutoryRules_Id",
                        column: x => x.Id,
                        principalTable: "StatutoryRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PayDivisorRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SalaryBasis = table.Column<int>(type: "INTEGER", nullable: false),
                    DaysInPeriod = table.Column<long>(type: "INTEGER", nullable: true),
                    HoursInPeriod = table.Column<long>(type: "INTEGER", nullable: true),
                    EmploymentTypeCode = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayDivisorRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayDivisorRules_StatutoryRules_Id",
                        column: x => x.Id,
                        principalTable: "StatutoryRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PayrollInputSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayrollRunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayrollRunEmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SnapshotJson = table.Column<string>(type: "TEXT", nullable: false),
                    SnapshotHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EngineVersion = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CapturedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SealedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollInputSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayrollRunInputSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayrollRunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayrollRunEmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InputType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    InputId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ApprovedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollRunInputSources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Timesheets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayrollPeriodId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ApprovalStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    SubmittedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ApprovedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    DecisionReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ConsumedByPayrollRunId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CorrectsTimesheetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CorrectionReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Timesheets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LoanInstalments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeLoanId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstalmentNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PayrollPeriodId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    PrincipalPortion = table.Column<long>(type: "INTEGER", nullable: false),
                    InterestPortion = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    PayrollRunId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DeductedOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    SkipReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoanInstalments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoanInstalments_EmployeeLoans_EmployeeLoanId",
                        column: x => x.EmployeeLoanId,
                        principalTable: "EmployeeLoans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LoanTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeLoanId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TransactionType = table.Column<int>(type: "INTEGER", nullable: false),
                    Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    TransactionDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    LoanInstalmentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PayrollRunId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PayrollPeriodId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    IsReversed = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReversedByTransactionId = table.Column<Guid>(type: "TEXT", nullable: true),
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
                    table.PrimaryKey("PK_LoanTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoanTransactions_EmployeeLoans_EmployeeLoanId",
                        column: x => x.EmployeeLoanId,
                        principalTable: "EmployeeLoans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PublicHolidays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    HolidayCalendarId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    ActualDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    VerificationStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicHolidays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PublicHolidays_HolidayCalendars_HolidayCalendarId",
                        column: x => x.HolidayCalendarId,
                        principalTable: "HolidayCalendars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LeaveEntitlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LeaveTypeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LeaveYearStart = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    LeaveYearEnd = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EntitlementDays = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveEntitlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeaveEntitlements_LeaveTypes_LeaveTypeId",
                        column: x => x.LeaveTypeId,
                        principalTable: "LeaveTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeaveRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LeaveTypeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Days = table.Column<long>(type: "INTEGER", nullable: false),
                    HolidayCalendarId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsPaid = table.Column<bool>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ApprovalStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    SubmittedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ApprovedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    DecisionReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ConsumedByPayrollRunId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CorrectsLeaveRequestId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CorrectionReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeaveRequests_LeaveTypes_LeaveTypeId",
                        column: x => x.LeaveTypeId,
                        principalTable: "LeaveTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TimeEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TimesheetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProjectSiteId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProjectName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    ProjectSiteName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    OrdinaryHours = table.Column<long>(type: "INTEGER", nullable: false),
                    DaysWorked = table.Column<long>(type: "INTEGER", nullable: false),
                    IsPublicHoliday = table.Column<bool>(type: "INTEGER", nullable: false),
                    PublicHolidayId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsAbsence = table.Column<bool>(type: "INTEGER", nullable: false),
                    LeaveRequestId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimeEntries_Timesheets_TimesheetId",
                        column: x => x.TimesheetId,
                        principalTable: "Timesheets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LeaveTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LeaveEntitlementId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LeaveTypeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TransactionType = table.Column<int>(type: "INTEGER", nullable: false),
                    Days = table.Column<long>(type: "INTEGER", nullable: false),
                    TransactionDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    EffectiveTo = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    LeaveRequestId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PayrollRunId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    IsReversed = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReversedByTransactionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReversalReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeaveTransactions_LeaveEntitlements_LeaveEntitlementId",
                        column: x => x.LeaveEntitlementId,
                        principalTable: "LeaveEntitlements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TimeEntryOvertimeLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TimeEntryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OvertimeCategoryCode = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Hours = table.Column<long>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeEntryOvertimeLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimeEntryOvertimeLines_TimeEntries_TimeEntryId",
                        column: x => x.TimeEntryId,
                        principalTable: "TimeEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeLoans_CompanyId_LoanNumber",
                table: "EmployeeLoans",
                columns: new[] { "CompanyId", "LoanNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeLoans_EmployeeId_Status",
                table: "EmployeeLoans",
                columns: new[] { "EmployeeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_HolidayCalendars_CompanyId_Code",
                table: "HolidayCalendars",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaveEntitlements_EmployeeId_LeaveTypeId_LeaveYearStart",
                table: "LeaveEntitlements",
                columns: new[] { "EmployeeId", "LeaveTypeId", "LeaveYearStart" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaveEntitlements_LeaveTypeId",
                table: "LeaveEntitlements",
                column: "LeaveTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_CompanyId_ApprovalStatus",
                table: "LeaveRequests",
                columns: new[] { "CompanyId", "ApprovalStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_EmployeeId_StartDate_EndDate",
                table: "LeaveRequests",
                columns: new[] { "EmployeeId", "StartDate", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_LeaveTypeId",
                table: "LeaveRequests",
                column: "LeaveTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveTransactions_LeaveEntitlementId",
                table: "LeaveTransactions",
                column: "LeaveEntitlementId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveTransactions_LeaveRequestId",
                table: "LeaveTransactions",
                column: "LeaveRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveTypes_CompanyId_Code",
                table: "LeaveTypes",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoanInstalments_EmployeeLoanId_InstalmentNumber",
                table: "LoanInstalments",
                columns: new[] { "EmployeeLoanId", "InstalmentNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoanInstalments_PayrollPeriodId_Status",
                table: "LoanInstalments",
                columns: new[] { "PayrollPeriodId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_LoanTransactions_EmployeeLoanId",
                table: "LoanTransactions",
                column: "EmployeeLoanId");

            migrationBuilder.CreateIndex(
                name: "IX_LoanTransactions_PayrollRunId",
                table: "LoanTransactions",
                column: "PayrollRunId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollInputSnapshots_PayrollRunEmployeeId",
                table: "PayrollInputSnapshots",
                column: "PayrollRunEmployeeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollInputSnapshots_PayrollRunId",
                table: "PayrollInputSnapshots",
                column: "PayrollRunId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRunInputSources_InputType_InputId",
                table: "PayrollRunInputSources",
                columns: new[] { "InputType", "InputId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRunInputSources_PayrollRunId_InputType",
                table: "PayrollRunInputSources",
                columns: new[] { "PayrollRunId", "InputType" });

            migrationBuilder.CreateIndex(
                name: "IX_PublicHolidays_HolidayCalendarId_Date",
                table: "PublicHolidays",
                columns: new[] { "HolidayCalendarId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_TimesheetId_WorkDate",
                table: "TimeEntries",
                columns: new[] { "TimesheetId", "WorkDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntryOvertimeLines_TimeEntryId_OvertimeCategoryCode",
                table: "TimeEntryOvertimeLines",
                columns: new[] { "TimeEntryId", "OvertimeCategoryCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Timesheets_CompanyId_ApprovalStatus",
                table: "Timesheets",
                columns: new[] { "CompanyId", "ApprovalStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_Timesheets_EmployeeId_PayrollPeriodId_CorrectsTimesheetId",
                table: "Timesheets",
                columns: new[] { "EmployeeId", "PayrollPeriodId", "CorrectsTimesheetId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeaveRequests");

            migrationBuilder.DropTable(
                name: "LeaveTransactions");

            migrationBuilder.DropTable(
                name: "LoanInstalments");

            migrationBuilder.DropTable(
                name: "LoanTransactions");

            migrationBuilder.DropTable(
                name: "OvertimeRules");

            migrationBuilder.DropTable(
                name: "PayDivisorRules");

            migrationBuilder.DropTable(
                name: "PayrollInputSnapshots");

            migrationBuilder.DropTable(
                name: "PayrollRunInputSources");

            migrationBuilder.DropTable(
                name: "PublicHolidays");

            migrationBuilder.DropTable(
                name: "TimeEntryOvertimeLines");

            migrationBuilder.DropTable(
                name: "LeaveEntitlements");

            migrationBuilder.DropTable(
                name: "EmployeeLoans");

            migrationBuilder.DropTable(
                name: "HolidayCalendars");

            migrationBuilder.DropTable(
                name: "TimeEntries");

            migrationBuilder.DropTable(
                name: "LeaveTypes");

            migrationBuilder.DropTable(
                name: "Timesheets");
        }
    }
}
