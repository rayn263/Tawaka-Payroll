using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tawaka.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompanyEmployeesAndSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "StatutoryRules",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "PayrollPeriods",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Clients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ContactName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AddressLine1 = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Companies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LegalName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TradingName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    RegistrationNumber = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    AddressLine1 = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AddressLine2 = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    City = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Country = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Website = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    LogoPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    TaxNumber = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    PayeReference = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    NssaEmployerNumber = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    ZimdefNumber = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    StandardsDevelopmentFundNumber = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    NecCode = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    NecMembershipNumber = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    IndustryClassification = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ApwcsIndustryCode = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    DefaultPayrollCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    DefaultReportingCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    AllowMixedCurrencyPayroll = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeductionTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Category = table.Column<int>(type: "INTEGER", nullable: false),
                    ReducesTaxableIncome = table.Column<bool>(type: "INTEGER", nullable: false),
                    AppliesBeforeTax = table.Column<bool>(type: "INTEGER", nullable: false),
                    HasLimit = table.Column<bool>(type: "INTEGER", nullable: false),
                    LimitAmount = table.Column<long>(type: "INTEGER", nullable: true),
                    LimitCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: true),
                    TreatmentVerificationStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    TreatmentSource = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    TreatmentNotes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    GlAccountCode = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    ShowOnPayslipWhenZero = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsSystemType = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeductionTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ManagerEmployeeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ParentDepartmentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CostCentreCode = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EarningTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Category = table.Column<int>(type: "INTEGER", nullable: false),
                    IsTaxable = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsNssaApplicable = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsIncludedInGross = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsPensionable = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsEmployerLevyBase = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsExemptUpToLimit = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExemptionRuleId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsReimbursive = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequiresExpenseProof = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsBenefitInKind = table.Column<bool>(type: "INTEGER", nullable: false),
                    ValuationMethod = table.Column<int>(type: "INTEGER", nullable: false),
                    TreatmentVerificationStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    TreatmentSource = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    TreatmentNotes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    DefaultCalculationBasis = table.Column<int>(type: "INTEGER", nullable: false),
                    DefaultMultiplier = table.Column<long>(type: "INTEGER", nullable: true),
                    GlAccountCode = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    ShowOnPayslipWhenZero = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsSystemType = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EarningTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentType = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    FileHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ExpiryDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeDocuments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeNextOfKin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FullName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Relationship = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    Address = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    IsEmergencyContact = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsBeneficiary = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeNextOfKin", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeNumber = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    FirstName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    MiddleName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    LastName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    NationalId = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    PassportNumber = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    DateOfBirth = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Gender = table.Column<int>(type: "INTEGER", nullable: false),
                    MaritalStatus = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    Nationality = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    AlternatePhone = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AddressLine1 = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AddressLine2 = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    City = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    PhotoPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    HireDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    TerminationDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    TerminationReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeStatusHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FromStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    ToStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ChangedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeStatusHistory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmploymentTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    DefaultPaymentFrequency = table.Column<int>(type: "INTEGER", nullable: false),
                    DefaultEarningsBasis = table.Column<int>(type: "INTEGER", nullable: false),
                    RequiresContractEndDate = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequiresTimesheet = table.Column<bool>(type: "INTEGER", nullable: false),
                    AccruesLeave = table.Column<bool>(type: "INTEGER", nullable: false),
                    EngagementWarningDays = table.Column<int>(type: "INTEGER", nullable: true),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmploymentTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JobTitles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Grade = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobTitles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Locations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    AddressLine1 = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AddressLine2 = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    City = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Locations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LoginAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    AttemptedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Succeeded = table.Column<bool>(type: "INTEGER", nullable: false),
                    FailureReason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Machine = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoginAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Permissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    ConflictsWith = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsSystemRole = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Username = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    FullName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    PasswordChangedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    MustChangePassword = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    FailedLoginCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LockedUntil = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CompanyBankAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    BankName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    BranchName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    BranchCode = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    AccountNumber = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    SwiftCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    Purpose = table.Column<int>(type: "INTEGER", nullable: false),
                    IsDefaultForPurpose = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyBankAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyBankAccounts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CompanyCurrencies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDefaultPayrollCurrency = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDefaultReportingCurrency = table.Column<bool>(type: "INTEGER", nullable: false),
                    DecimalPlacesOverride = table.Column<int>(type: "INTEGER", nullable: true),
                    DisplayCodeOverride = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyCurrencies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyCurrencies_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeRecurringDeductions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeductionTypeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    CalculationBasis = table.Column<int>(type: "INTEGER", nullable: false),
                    Value = table.Column<long>(type: "INTEGER", nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeRecurringDeductions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeRecurringDeductions_DeductionTypes_DeductionTypeId",
                        column: x => x.DeductionTypeId,
                        principalTable: "DeductionTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeRecurringEarnings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EarningTypeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    CalculationBasis = table.Column<int>(type: "INTEGER", nullable: false),
                    Value = table.Column<long>(type: "INTEGER", nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeRecurringEarnings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeRecurringEarnings_EarningTypes_EarningTypeId",
                        column: x => x.EarningTypeId,
                        principalTable: "EarningTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmployeePaymentAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PaymentMethod = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    AccountName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    BankName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    BranchName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    BranchCode = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    AccountNumber = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    MobileMoneyProvider = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    MobileMoneyNumber = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    AllocationType = table.Column<int>(type: "INTEGER", nullable: false),
                    AllocationValue = table.Column<long>(type: "INTEGER", nullable: true),
                    IsPrimary = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeePaymentAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeePaymentAccounts_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeStatutoryProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaxNumber = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    IsPayeExempt = table.Column<bool>(type: "INTEGER", nullable: false),
                    PayeExemptionReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    NssaNumber = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    NssaEligibilityOverride = table.Column<bool>(type: "INTEGER", nullable: true),
                    NssaEligibilityOverrideReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsElderlyCreditEligible = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDisabledCreditEligible = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsBlindCreditEligible = table.Column<bool>(type: "INTEGER", nullable: false),
                    MedicalAidCreditApplies = table.Column<bool>(type: "INTEGER", nullable: false),
                    NecMembershipNumber = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    IsNecMember = table.Column<bool>(type: "INTEGER", nullable: false),
                    PensionSchemeNumber = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeStatutoryProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeStatutoryProfiles_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeContracts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VersionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    ContractReference = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    EmploymentTypeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobTitleId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DepartmentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LocationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProjectSiteId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    PayrollCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    PaymentFrequency = table.Column<int>(type: "INTEGER", nullable: false),
                    EarningsBasis = table.Column<int>(type: "INTEGER", nullable: false),
                    BasicSalary = table.Column<long>(type: "INTEGER", nullable: true),
                    HourlyRate = table.Column<long>(type: "INTEGER", nullable: true),
                    DailyRate = table.Column<long>(type: "INTEGER", nullable: true),
                    WeeklyRate = table.Column<long>(type: "INTEGER", nullable: true),
                    MonthlyRate = table.Column<long>(type: "INTEGER", nullable: true),
                    ProjectRate = table.Column<long>(type: "INTEGER", nullable: true),
                    CommissionStructure = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    StandardHoursPerDay = table.Column<long>(type: "INTEGER", nullable: false),
                    StandardDaysPerWeek = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    IsCurrent = table.Column<bool>(type: "INTEGER", nullable: false),
                    PreviousContractId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SupersededByContractId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ChangeReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeContracts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeContracts_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeContracts_EmploymentTypes_EmploymentTypeId",
                        column: x => x.EmploymentTypeId,
                        principalTable: "EmploymentTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ClientId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LocationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ContractValueAmount = table.Column<long>(type: "INTEGER", nullable: true),
                    ContractValueCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Projects_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Projects_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RolePermissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RoleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PermissionId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RolePermissions_Permissions_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "Permissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RolePermissions_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RoleId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserRoles_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserRoles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeProjectAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectSiteId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    AllocationPercent = table.Column<long>(type: "INTEGER", nullable: false),
                    RateOverrideAmount = table.Column<long>(type: "INTEGER", nullable: true),
                    RateOverrideCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeProjectAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeProjectAssignments_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmployeeProjectAssignments_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProjectSites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AddressLine1 = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    City = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    LocationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectSites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectSites_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Clients_CompanyId_Code",
                table: "Clients",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanyBankAccounts_CompanyId_AccountNumber_CurrencyCode",
                table: "CompanyBankAccounts",
                columns: new[] { "CompanyId", "AccountNumber", "CurrencyCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanyCurrencies_CompanyId_CurrencyCode",
                table: "CompanyCurrencies",
                columns: new[] { "CompanyId", "CurrencyCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeductionTypes_CompanyId_Code",
                table: "DeductionTypes",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Departments_CompanyId_Code",
                table: "Departments",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EarningTypes_CompanyId_Code",
                table: "EarningTypes",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EarningTypes_TreatmentVerificationStatus",
                table: "EarningTypes",
                column: "TreatmentVerificationStatus");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeContracts_EmployeeId_VersionNumber",
                table: "EmployeeContracts",
                columns: new[] { "EmployeeId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeContracts_EmploymentTypeId",
                table: "EmployeeContracts",
                column: "EmploymentTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeContracts_OneCurrentPerEmployee",
                table: "EmployeeContracts",
                column: "EmployeeId",
                unique: true,
                filter: "[IsCurrent] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeDocuments_EmployeeId",
                table: "EmployeeDocuments",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeePaymentAccounts_OnePrimaryPerCurrency",
                table: "EmployeePaymentAccounts",
                columns: new[] { "EmployeeId", "CurrencyCode" },
                unique: true,
                filter: "[IsPrimary] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeProjectAssignments_EmployeeId_ProjectId_StartDate",
                table: "EmployeeProjectAssignments",
                columns: new[] { "EmployeeId", "ProjectId", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeProjectAssignments_ProjectId",
                table: "EmployeeProjectAssignments",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeRecurringDeductions_DeductionTypeId",
                table: "EmployeeRecurringDeductions",
                column: "DeductionTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeRecurringDeductions_EmployeeId_DeductionTypeId_EffectiveFrom",
                table: "EmployeeRecurringDeductions",
                columns: new[] { "EmployeeId", "DeductionTypeId", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeRecurringEarnings_EarningTypeId",
                table: "EmployeeRecurringEarnings",
                column: "EarningTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeRecurringEarnings_EmployeeId_EarningTypeId_EffectiveFrom",
                table: "EmployeeRecurringEarnings",
                columns: new[] { "EmployeeId", "EarningTypeId", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_Employees_CompanyId_EmployeeNumber",
                table: "Employees",
                columns: new[] { "CompanyId", "EmployeeNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Employees_CompanyId_NationalId",
                table: "Employees",
                columns: new[] { "CompanyId", "NationalId" },
                unique: true,
                filter: "[NationalId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_Status",
                table: "Employees",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeStatusHistory_EmployeeId_EffectiveDate",
                table: "EmployeeStatusHistory",
                columns: new[] { "EmployeeId", "EffectiveDate" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeStatutoryProfiles_EmployeeId",
                table: "EmployeeStatutoryProfiles",
                column: "EmployeeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmploymentTypes_CompanyId_Code",
                table: "EmploymentTypes",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobTitles_CompanyId_Code",
                table: "JobTitles",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Locations_CompanyId_Code",
                table: "Locations",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoginAttempts_Username_AttemptedAt",
                table: "LoginAttempts",
                columns: new[] { "Username", "AttemptedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Permissions_Code",
                table: "Permissions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Projects_ClientId",
                table: "Projects",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_CompanyId_Code",
                table: "Projects",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Projects_LocationId",
                table: "Projects",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectSites_CompanyId_Code",
                table: "ProjectSites",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectSites_ProjectId",
                table: "ProjectSites",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_PermissionId",
                table: "RolePermissions",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_RoleId_PermissionId",
                table: "RolePermissions",
                columns: new[] { "RoleId", "PermissionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Roles_Name",
                table: "Roles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_RoleId",
                table: "UserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_UserId_RoleId",
                table: "UserRoles",
                columns: new[] { "UserId", "RoleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanyBankAccounts");

            migrationBuilder.DropTable(
                name: "CompanyCurrencies");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.DropTable(
                name: "EmployeeContracts");

            migrationBuilder.DropTable(
                name: "EmployeeDocuments");

            migrationBuilder.DropTable(
                name: "EmployeeNextOfKin");

            migrationBuilder.DropTable(
                name: "EmployeePaymentAccounts");

            migrationBuilder.DropTable(
                name: "EmployeeProjectAssignments");

            migrationBuilder.DropTable(
                name: "EmployeeRecurringDeductions");

            migrationBuilder.DropTable(
                name: "EmployeeRecurringEarnings");

            migrationBuilder.DropTable(
                name: "EmployeeStatusHistory");

            migrationBuilder.DropTable(
                name: "EmployeeStatutoryProfiles");

            migrationBuilder.DropTable(
                name: "JobTitles");

            migrationBuilder.DropTable(
                name: "LoginAttempts");

            migrationBuilder.DropTable(
                name: "ProjectSites");

            migrationBuilder.DropTable(
                name: "RolePermissions");

            migrationBuilder.DropTable(
                name: "UserRoles");

            migrationBuilder.DropTable(
                name: "Companies");

            migrationBuilder.DropTable(
                name: "EmploymentTypes");

            migrationBuilder.DropTable(
                name: "DeductionTypes");

            migrationBuilder.DropTable(
                name: "EarningTypes");

            migrationBuilder.DropTable(
                name: "Employees");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropTable(
                name: "Permissions");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Clients");

            migrationBuilder.DropTable(
                name: "Locations");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "StatutoryRules");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "PayrollPeriods");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "AppSettings");
        }
    }
}
