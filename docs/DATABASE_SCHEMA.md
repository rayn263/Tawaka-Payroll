# Tawaka Payroll — Database Schema

**Document status:** Proposal, awaiting approval
**Last updated:** 2026-09-15

Conventions:

- Table names `PascalCase` plural; primary key `Id` (`GUID`/`uniqueidentifier`, or `BLOB` on
  SQLite) unless stated.
- Every business table carries `CreatedAt`, `CreatedBy`, `ModifiedAt`, `ModifiedBy`,
  `RowVersion` (optimistic concurrency). Written automatically by the audit interceptor.
- Money columns appear as **pairs**: `<Name>Amount DECIMAL(18,4)` + `<Name>Currency CHAR(3)`.
  On SQLite the amount persists as a scaled integer via a value converter.
- Soft delete (`IsActive`, `DeactivatedAt`, `DeactivationReason`) is used for master data.
  **Payroll transaction records are never deleted**, only reversed.
- Rule tables are **versioned**: `EffectiveFrom DATE NOT NULL`, `EffectiveTo DATE NULL`,
  `Status` (`Draft`/`Active`/`Superseded`), and never edited once used by a finalised run — a new
  version is created instead.

---

## 1. Security and audit

| Table | Key columns |
|---|---|
| **Users** | `Id`, `Username` (unique), `FullName`, `Email`, `PasswordHash`, `PasswordChangedAt`, `MustChangePassword`, `IsActive`, `FailedLoginCount`, `LockedUntil`, `LastLoginAt` |
| **Roles** | `Id`, `Name` (unique), `Description`, `IsSystemRole` |
| **Permissions** | `Id`, `Code` (unique, e.g. `Payroll.Approve`), `Category`, `Description` |
| **RolePermissions** | `RoleId` → Roles, `PermissionId` → Permissions (composite PK) |
| **UserRoles** | `UserId` → Users, `RoleId` → Roles (composite PK) |
| **AuditLogs** | `Id`, `OccurredAt`, `UserId`, `UserName` (denormalised — survives user deletion), `Action` (`Create`/`Update`/`Delete`/`Approve`/`Lock`/`Reopen`/`Login`/`Export`), `EntityName`, `EntityId`, `FieldName`, `OldValue`, `NewValue`, `Reason`, `IpOrMachine`, `CorrelationId`. **Append-only, no update or delete path.** |
| **LoginAttempts** | `Id`, `Username`, `AttemptedAt`, `Succeeded`, `Machine` |

---

## 2. Company and organisation

| Table | Key columns |
|---|---|
| **Companies** | `Id`, `LegalName`, `TradingName`, `RegistrationNumber`, `TaxNumber` (BP/TIN), `NssaEmployerNumber`, `ZimdefNumber`, `NecCode`, `IndustryClassification`, `ApwcsIndustryCode`, `AddressLine1/2`, `City`, `Phone`, `Email`, `LogoPath`, `IsActive`. *(Multi-company ready from day one — every scoped table carries `CompanyId`.)* |
| **CompanySettings** | `Id`, `CompanyId`, `Key`, `Value`, `DataType`, `Category` (`Payroll`/`Tax`/`Payslip`/`Security`/`Currency`). Typed accessors in code; key/value avoids a migration per new setting. |
| **CompanyBankAccounts** | `Id`, `CompanyId`, `AccountName`, `BankName`, `BranchCode`, `AccountNumber`, `Currency`, `IsDefaultFor` (`Net pay`/`PAYE`/`NSSA`), `IsActive` |
| **Locations** | `Id`, `CompanyId`, `Name`, `Address`, `IsActive` |
| **Departments** | `Id`, `CompanyId`, `Code`, `Name`, `ParentDepartmentId`, `CostCentreCode`, `IsActive` |
| **JobTitles** | `Id`, `CompanyId`, `Name`, `Grade`, `IsActive` |
| **Clients** | `Id`, `CompanyId`, `Name`, `ContactDetails`, `IsActive` |
| **Projects** | `Id`, `CompanyId`, `Code`, `Name`, `ClientId`, `LocationId`, `StartDate`, `EndDate`, `ContractValueAmount/Currency`, `Status`, `IsActive` |

---

## 3. Reference data

| Table | Key columns |
|---|---|
| **Currencies** | `Code` (PK, `USD`, `ZWG`), `Name` (`US Dollar`, `Zimbabwe Gold`), `DisplayCode` (`USD`, `ZiG` — the label shown to users), `Symbol`, `DecimalPlaces`, `RoundingMode`, `IsActive`, `SortOrder`. ISO code is the key; `DisplayCode` carries the local `ZiG` terminology. |
| **ExchangeRates** | `Id`, `FromCurrency`, `ToCurrency`, `Rate DECIMAL(18,8)`, `RateType` (`Interbank`/`Auction`/`Official`/`Custom`), `Source` (free text, e.g. "RBZ interbank"), `SourceReference`, `RateDate`, `EffectiveFrom`, `EffectiveTo`, `EnteredBy`, `EnteredAt`, `IsActive`. Unique on (`FromCurrency`,`ToCurrency`,`RateType`,`RateDate`). |
| **EmploymentTypes** | `Id`, `Code`, `Name`, `DefaultPaymentFrequency`, `DefaultEarningsBasis`, `RequiresContractEndDate`, `RequiresTimesheet`, `AccruesLeave`, `NssaDefaultEligible`, `IsActive`. Seeded with the 11 types; user-extensible. |
| **PaymentMethods** | `Id`, `Code` (`BankTransfer`/`MobileMoney`/`Cash`/`Cheque`), `Name`, `RequiresBankAccount`, `IsActive` |
| **Banks** | `Id`, `Name`, `BranchName`, `BranchCode`, `SwiftCode`, `IsActive` |
| **PublicHolidays** | `Id`, `CompanyId`, `Date`, `Name`, `IsPaid`, `Year` |

---

## 4. Employees

| Table | Key columns |
|---|---|
| **Employees** | `Id`, `CompanyId`, `EmployeeNumber` (unique per company), `FirstName`, `MiddleName`, `LastName`, `NationalId`, `PassportNumber`, `DateOfBirth`, `Gender`, `MaritalStatus`, `Nationality`, `Phone`, `AlternatePhone`, `Email`, `AddressLine1/2`, `City`, `PhotoPath`, `Status` (`Active`/`Inactive`/`Suspended`/`Terminated`), `HireDate`, `TerminationDate`, `TerminationReason`, `Notes`, `IsActive` |
| **EmployeeContracts** | `Id`, `EmployeeId`, `EmploymentTypeId`, `JobTitleId`, `DepartmentId`, `LocationId`, `ProjectId`, `StartDate`, `EndDate`, `PaymentFrequency` (`Monthly`/`Weekly`/`Fortnightly`/`Daily`/`ProjectBased`/`Custom`), `PayrollCurrency`, `EarningsBasis`, `BasicSalaryAmount/Currency`, `HourlyRateAmount/Currency`, `DailyRateAmount/Currency`, `WeeklyRateAmount/Currency`, `MonthlyRateAmount/Currency`, `ProjectRateAmount/Currency`, `CommissionStructureJson`, `StandardHoursPerDay`, `StandardDaysPerWeek`, `IsCurrent`, `SupersededByContractId`, `ChangeReason`. **Employment terms are versioned, not overwritten** — this is how salary history stays correct. |
| **EmployeeStatutoryProfiles** | `Id`, `EmployeeId`, `TaxNumber` (BP/TIN), `TaxStatus`, `IsPayeExempt`, `PayeExemptionReason`, `NssaNumber`, `IsNssaEligible`, `NssaEligibilityOverrideReason`, `IsElderlyCreditEligible`, `IsDisabledCreditEligible`, `IsBlindCreditEligible`, `MedicalAidCreditApplies`, `NecMembershipNumber`, `PensionSchemeNumber` |
| **EmployeeBankAccounts** | `Id`, `EmployeeId`, `PaymentMethodId`, `BankId`, `AccountName`, `AccountNumber`, `Currency`, `MobileMoneyProvider`, `MobileMoneyNumber`, `AllocationType` (`Full`/`Percentage`/`FixedAmount`), `AllocationValue`, `IsPrimary`, `IsActive`. Supports separate USD and ZiG accounts for one employee. |
| **EmployeeRecurringEarnings** | `Id`, `EmployeeId`, `EarningTypeId`, `Amount/Currency`, `CalculationBasis` (`Fixed`/`PercentOfBasic`/`RatePerUnit`), `Value`, `EffectiveFrom`, `EffectiveTo`, `IsActive` |
| **EmployeeRecurringDeductions** | `Id`, `EmployeeId`, `DeductionTypeId`, `Amount/Currency`, `CalculationBasis`, `Value`, `EffectiveFrom`, `EffectiveTo`, `Priority`, `IsActive` |
| **EmployeeProjectAssignments** | `Id`, `EmployeeId`, `ProjectId`, `StartDate`, `EndDate`, `RateOverrideAmount/Currency`, `AllocationPercent`, `IsActive` |
| **EmployeeNextOfKin** | `Id`, `EmployeeId`, `FullName`, `Relationship`, `Phone`, `Address`, `IsEmergencyContact`, `IsBeneficiary` |
| **EmployeeDocuments** | `Id`, `EmployeeId`, `DocumentType`, `Title`, `FilePath`, `FileHash`, `ExpiryDate`, `UploadedBy`, `UploadedAt` |
| **EmployeeStatusHistory** | `Id`, `EmployeeId`, `FromStatus`, `ToStatus`, `EffectiveDate`, `Reason`, `ChangedBy`, `ChangedAt` |

An employee set to `Inactive` retains every historical payroll record; inactivity only excludes
them from new payroll runs.

---

## 5. Payroll configuration

| Table | Key columns |
|---|---|
| **EarningTypes** | `Id`, `CompanyId`, `Code`, `Name`, `Category` (`Basic`/`Overtime`/`Allowance`/`Bonus`/`Commission`/`Other`), `IsTaxable`, `IsNssaApplicable`, `IsIncludedInGross`, `IsPensionable`, `IsEmployerLevyBase`, `IsExemptUpToLimit`, `ExemptionRuleId`, `DefaultCalculationBasis`, `DefaultMultiplier` (e.g. 1.5 for overtime, 2.0 for public holiday), `GlAccountCode`, `DisplayOrder`, `ShowOnPayslipWhenZero`, `IsSystemType`, `IsActive` |
| **DeductionTypes** | `Id`, `CompanyId`, `Code`, `Name`, `Category` (`Statutory`/`Loan`/`Advance`/`Benefit`/`Union`/`Garnishment`/`Other`), `ReducesTaxableIncome`, `AppliesBeforeTax`, `HasLimit`, `LimitRuleId`, `Priority`, `GlAccountCode`, `DisplayOrder`, `ShowOnPayslipWhenZero`, `IsSystemType`, `IsActive` |
| **EmployerCostTypes** | `Id`, `CompanyId`, `Code` (`NSSA_POBS_ER`, `APWCS`, `ZIMDEF`, `SDF`, `NEC_ER`, …), `Name`, `RuleTable`, `GlAccountCode`, `DisplayOnPayslip`, `IsActive` |
| **PayrollCalendars** | `Id`, `CompanyId`, `Name`, `Frequency`, `PeriodStartDay`, `PayDayRule`, `IsDefault`, `IsActive` |
| **PayrollPeriods** | `Id`, `CompanyId`, `CalendarId`, `Code` (`2026-09`), `Name` (`September 2026`), `Frequency`, `StartDate`, `EndDate`, `PayDate`, `TaxYear`, `TaxPeriodNumber`, `Status` (`Open`/`Closed`/`Locked`), `IsActive`. Unique on (`CompanyId`,`CalendarId`,`Code`). |

---

## 6. Statutory rules — all versioned

| Table | Key columns |
|---|---|
| **TaxRules** | `Id`, `Name`, `TaxYear`, `Currency`, `PeriodBasis` (`Monthly`/`Weekly`/`Fortnightly`/`Daily`/`Annual`), `CalculationMethod` (`PeriodTable`/`CumulativeAnnual`), `EffectiveFrom`, `EffectiveTo`, `Status`, `SourceReference`, `VerificationStatus`, `VerifiedBy`, `VerifiedAt`, `Notes` |
| **TaxBrackets** | `Id`, `TaxRuleId`, `Sequence`, `LowerBoundAmount`, `UpperBoundAmount` (null = ∞), `Rate DECIMAL(9,6)`, `FixedDeductionAmount` (the "less" column in ZIMRA-style tables), `CumulativeTaxAtLowerBound` |
| **TaxCreditRules** | `Id`, `CreditType` (`Elderly`/`Disabled`/`Blind`/`MedicalAidContribution`/`MedicalExpense`), `Currency`, `Amount`, `PeriodBasis`, `PercentageOfQualifyingAmount`, `AnnualCap`, `EffectiveFrom`, `EffectiveTo`, `Status`, `SourceReference`, `VerificationStatus` |
| **TaxExemptionRules** | `Id`, `ExemptionType` (`AnnualBonus`/`RetrenchmentPackage`/`Other`), `Currency`, `LimitAmount`, `PeriodBasis`, `EffectiveFrom`, `EffectiveTo`, `Status`, `SourceReference`, `VerificationStatus` |
| **AidsLevyRules** | `Id`, `Rate`, `Base` (`TaxAfterCredits`/`TaxBeforeCredits`), `Currency` (null = all), `EffectiveFrom`, `EffectiveTo`, `Status`, `SourceReference`, `VerificationStatus` |
| **NssaRules** | `Id`, `Scheme` (`POBS`), `EmployeeRate`, `EmployerRate`, `InsurableEarningsCeilingAmount/Currency`, `MinimumAge`, `MaximumAge`, `EarningsBasis` (`BasicOnly`/`FlaggedEarnings`), `CeilingPeriodBasis`, `EffectiveFrom`, `EffectiveTo`, `Status`, `SourceReference`, `VerificationStatus` |
| **NssaEligibilityRules** | `Id`, `NssaRuleId`, `EmploymentTypeId`, `IsEligible`, `ConditionExpression`, `Notes`. Lets casual/temporary treatment be configured rather than assumed. |
| **ApwcsRules** | `Id`, `IndustryClassification`, `IndustryCode`, `Rate`, `Base` (`BasicEarnings`/`GrossEarnings`), `CeilingAmount/Currency`, `EffectiveFrom`, `EffectiveTo`, `Status`, `SourceReference`, `VerificationStatus` |
| **EmployerLevyRules** | `Id`, `LevyType` (`ZIMDEF`/`SDF`/`NEC`/`Other`), `Name`, `Rate`, `Base` (`GrossWageBill`/`LeviableWageBill`/`BasicEarnings`), `EmployeePortionRate`, `EmployerPortionRate`, `Currency`, `EffectiveFrom`, `EffectiveTo`, `Status`, `SourceReference`, `VerificationStatus` |
| **CurrencyTaxStrategies** | `Id`, `StrategyName` (`SingleCurrency`/`AggregateInPrimaryCurrency`/`SeparatePerCurrency`), `PrimaryCurrency`, `RateDeterminationRule` (`PayDate`/`PeriodEnd`/`PaymentDate`/`Pinned`), `RateType`, `EffectiveFrom`, `EffectiveTo`, `Status`, `SourceReference`, `VerificationStatus`, `ApprovedByAdvisor`, `AdvisorReference` |

`VerificationStatus` (`Unverified`/`VerifiedAgainstOfficialSource`/`AdvisorConfirmed`) plus
`SourceReference` mean the system can always answer "where did this number come from?" — and the
dashboard can warn when an unverified rule is about to be used for a live payroll.

---

## 7. Payroll transactions

| Table | Key columns |
|---|---|
| **PayrollRuns** | `Id`, `CompanyId`, `PayrollPeriodId`, `RunNumber`, `RunType` (`Normal`/`Supplementary`/`Bonus`/`Correction`), `Status` (`Draft`/`Calculating`/`AwaitingReview`/`Approved`/`Finalised`/`Paid`/`Locked`), `CalculatedBy/At`, `ApprovedBy/At`, `FinalisedBy/At`, `PaidBy/At`, `LockedBy/At`, `ReopenedBy/At`, `ReopenReason`, `RuleSnapshotJson`, `ExchangeRateSnapshotJson`, `CurrencyTaxStrategyId`, `EngineVersion`, `Notes` |
| **PayrollRunEmployees** | `Id`, `PayrollRunId`, `EmployeeId`, `EmployeeContractId`, `EmploymentTypeId`, `DepartmentId`, `ProjectId`, `PayrollCurrency`, `DaysWorked`, `HoursWorked`, `GrossEarningsAmount`, `TaxableIncomeAmount`, `NssaInsurableEarningsAmount`, `PayeAmount`, `TaxCreditsAmount`, `AidsLevyAmount`, `NssaEmployeeAmount`, `TotalStatutoryDeductionsAmount`, `TotalOtherDeductionsAmount`, `TotalDeductionsAmount`, `NetPayAmount`, `TotalEmployerCostAmount`, `PaymentMethodId`, `PaymentStatus`, `IsExcluded`, `ExclusionReason`. *(The payslip header row. Snapshots the employee's details so later master-data edits never rewrite history.)* |
| **PayrollEarningLines** | `Id`, `PayrollRunEmployeeId`, `EarningTypeId`, `Description`, `Quantity`, `RateAmount/Currency`, `OriginalAmount/OriginalCurrency`, `ExchangeRateUsed`, `ExchangeRateId`, `ConvertedAmount/ConvertedCurrency`, `TaxableAmount`, `ExemptAmount`, `NssaApplicableAmount`, `IsTaxable`, `IsIncludedInGross`, `SourceType` (`Recurring`/`Manual`/`Timesheet`/`Leave`), `SourceReferenceId`, `DisplayOrder` |
| **PayrollDeductionLines** | `Id`, `PayrollRunEmployeeId`, `DeductionTypeId`, `Description`, `OriginalAmount/OriginalCurrency`, `ExchangeRateUsed`, `ConvertedAmount/ConvertedCurrency`, `Category`, `ReducesTaxableIncome`, `SourceType` (`Statutory`/`Recurring`/`Loan`/`Advance`/`Manual`), `SourceReferenceId`, `DisplayOrder` |
| **PayrollEmployerCostLines** | `Id`, `PayrollRunEmployeeId`, `EmployerCostTypeId`, `Description`, `BaseAmount`, `RateApplied`, `Amount/Currency`, `RuleId`, `RuleTable`, `DisplayOrder` |
| **PayrollCalculationTraces** | `Id`, `PayrollRunEmployeeId`, `Stage`, `Sequence`, `ItemKey`, `RuleTable`, `RuleId`, `InputsJson`, `IntermediateJson`, `OutputAmount`, `Explanation`. **The audit backbone of every figure.** |
| **PayrollWarnings** | `Id`, `PayrollRunId`, `PayrollRunEmployeeId`, `Severity` (`Info`/`Warning`/`Blocking`), `Code`, `Message`, `IsAcknowledged`, `AcknowledgedBy/At` |
| **PayrollApprovals** | `Id`, `PayrollRunId`, `Step`, `ActionedBy`, `ActionedAt`, `Decision` (`Approved`/`Rejected`), `Comments` |
| **Payslips** | `Id`, `PayrollRunEmployeeId`, `PayslipNumber` (unique), `Revision`, `GeneratedBy/At`, `FilePath`, `FileHash`, `TemplateVersion`, `SupersededByPayslipId`, `DeliveryStatus`, `DeliveredAt` |
| **PayrollPayments** | `Id`, `PayrollRunId`, `PayrollRunEmployeeId`, `Amount/Currency`, `PaymentDate`, `PaymentMethodId`, `CompanyBankAccountId`, `Reference`, `BatchId`, `Status`, `RecordedBy/At` |

---

## 8. Statutory obligations and payments

The heart of the "never claim something was paid when it wasn't" requirement.

| Table | Key columns |
|---|---|
| **StatutoryObligations** | `Id`, `CompanyId`, `PayrollPeriodId`, `PayrollRunId`, `Authority` (`ZIMRA`/`NSSA`/`ZIMDEF`/`SDF`/`NEC`), `ObligationType` (`PAYE`/`AIDS_LEVY`/`NSSA_POBS_EE`/`NSSA_POBS_ER`/`APWCS`/`ZIMDEF`/`SDF`/`NEC_EE`/`NEC_ER`), `Currency`, `CalculatedAmount`, `DeductedAmount`, `ApprovedAmount`, `PaidAmount`, `OutstandingAmount` (computed), `IsCalculated`, `IsDeducted`, `IsApproved`, `IsPaid`, `ApprovedBy/At`, `DueDate`, `Status` (`Calculated`/`Deducted`/`Approved`/`PartiallyPaid`/`Paid`/`Overdue`), `Notes`. Unique on (`PayrollRunId`,`ObligationType`,`Currency`). |
| **StatutoryObligationLines** | `Id`, `StatutoryObligationId`, `PayrollRunEmployeeId`, `EmployeeId`, `Amount`, `Currency`. Per-employee breakdown, so a remittance reconciles to the individuals behind it. |
| **StatutoryPayments** | `Id`, `StatutoryObligationId`, `Amount/Currency`, `PaymentDate`, `PaymentMethod`, `CompanyBankAccountId`, `PaymentReference`, `AuthorityReceiptNumber`, `ReceiptFilePath`, `ReceiptFileHash`, `RecordedBy/At`, `Notes`, `IsReversed`, `ReversalReason`. **Only the existence of a row here can set `IsPaid`.** Partial payments are supported; the obligation stays `PartiallyPaid` until fully settled. |
| **StatutoryReturns** | `Id`, `CompanyId`, `ReturnType` (`P2`/`P4`/`ITF16`/`ITF12B`/`Other`), `PeriodId`, `Currency`, `SubmittedBy/At`, `SubmissionReference`, `FilePath`, `Status` |

**State machine:** the four states are set by four distinct, separately permissioned actions.
Nothing in the application can set paid without a corresponding `StatutoryPayments` row carrying a
date, method and reference.

**As built at Milestone 4** (migration `StatutoryObligationsAndPayslips`), with three refinements
to the design above — all in the direction of the same principle:

- **`IsPaid` is not a column.** It is derived from the sum of unreversed payments (ADR-027), so
  there is no field for any code path to set. `PaidAmount` and `OutstandingAmount` are likewise
  computed from the payment rows rather than stored and maintained.
- **`IsDeductionApplicable`** was added. Employer-borne obligations — NSSA employer, APWCS, ZIMDEF,
  SDF — withhold nothing from anybody, so "deducted: no" would be a false negative on the register.
  They report `n/a` instead.
- **`StatutoryPayments` carries `PenaltyOrInterestIncluded`, `IsReversed`, `ReversalReason`,
  `ReversedBy` and `ReversedAt`.** A reversal keeps the row and its reason; the balance restores
  without the history disappearing. `PrincipalAmount` is the amount less penalty or interest, so a
  penalty is never mistaken for a settlement of the liability itself.

`StatutoryReturns` is **not** created yet: the return formats could not be obtained from an
authoritative source, and the table would only invite a guess at their contents.

**Payslips** (also Milestone 4): `Id`, `PayrollRunEmployeeId`, `PayslipNumber`, `Revision`,
`GeneratedBy/At`, `IssuedBy/At`, `IsDevelopmentCopy`, `SupersededByPayslipId`, `SupersedeReason`,
`FilePath`, `ContentHash`, `TemplateVersion`. The company is reached through the run employee, so
it is not duplicated here. The row records that a payslip was issued and which result it rendered; the document
itself is rebuilt from the payroll result each time it is opened, so a payslip can never drift from
the run it came from.

---

## 9. Time, leave, loans and advances *(built — Milestone 5)*

| Table | Key columns |
|---|---|
| **Timesheets** | `Id`, `EmployeeId`, `PayrollPeriodId`, `ProjectId`, `Status` (`Draft`/`Submitted`/`Approved`/`Rejected`/`Processed`), `SubmittedBy/At`, `ApprovedBy/At`, `TotalNormalHours`, `TotalOvertimeHours`, `TotalDaysWorked` |
| **TimesheetLines** | `Id`, `TimesheetId`, `WorkDate`, `ProjectId`, `NormalHours`, `OvertimeHours`, `SundayHours`, `PublicHolidayHours`, `NightShiftHours`, `DayType`, `AbsenceType`, `Notes` |
| **LeaveTypes** | `Id`, `CompanyId`, `Code`, `Name`, `IsPaid`, `PayPercentage`, `AccrualMethod`, `AccrualRatePerMonth`, `MaxCarryOverDays`, `MaxBalanceDays`, `RequiresApproval`, `RequiresDocument`, `AffectsPayroll`, `StatutoryBasis`, `IsActive` |
| **LeaveEntitlements** | `Id`, `EmployeeId`, `LeaveTypeId`, `Year`, `OpeningBalance`, `Accrued`, `Taken`, `Adjusted`, `ClosingBalance`, `ExpiryDate` |
| **LeaveRequests** | `Id`, `EmployeeId`, `LeaveTypeId`, `StartDate`, `EndDate`, `Days`, `Status`, `RequestedBy/At`, `ApprovedBy/At`, `Reason`, `DocumentPath`, `PayrollRunId` |
| **LeaveTransactions** | `Id`, `EmployeeId`, `LeaveTypeId`, `TransactionDate`, `Type` (`Accrual`/`Taken`/`Adjustment`/`Expiry`/`Encashment`), `Days`, `SourceReferenceId`, `Notes` |
| **LoanTypes** | `Id`, `CompanyId`, `Name`, `InterestMethod` (`None`/`Flat`/`Reducing`), `DefaultInterestRate`, `MaxTermMonths`, `DeductionTypeId`, `IsActive` |
| **Loans** | `Id`, `EmployeeId`, `LoanTypeId`, `LoanNumber`, `PrincipalAmount/Currency`, `InterestRate`, `InterestAmount`, `TotalRepayableAmount`, `IssuedDate`, `FirstDeductionPeriodId`, `NumberOfInstalments`, `InstalmentAmount`, `BalanceAmount`, `Status` (`Active`/`Suspended`/`SettledEarly`/`Completed`/`WrittenOff`), `ApprovedBy/At`, `Notes` |
| **LoanSchedules** | `Id`, `LoanId`, `InstalmentNumber`, `DuePeriodId`, `ScheduledAmount`, `PrincipalPortion`, `InterestPortion`, `Status`, `ActualDeductedAmount`, `PayrollRunEmployeeId` |
| **LoanTransactions** | `Id`, `LoanId`, `Date`, `Type` (`Disbursement`/`Deduction`/`ManualRepayment`/`Adjustment`/`WriteOff`), `Amount/Currency`, `BalanceAfter`, `PayrollRunEmployeeId`, `RecordedBy/At` |
| **Advances** | `Id`, `EmployeeId`, `AdvanceNumber`, `Amount/Currency`, `DateIssued`, `RecoveryPeriodId`, `RecoveredAmount`, `BalanceAmount`, `Status`, `ApprovedBy/At`, `PaymentMethodId`, `Reference` |

---

### 9a. As built at Milestone 5

Migration `TimeLeaveCalendarAndLoans`, 16 tables:

| Table | Notes |
|---|---|
| **Timesheets** | One per employee per period, carrying the input lifecycle (`ApprovalStatus`, submitted/approved actor and timestamp, `DecisionReason`) plus `ConsumedByPayrollRunId` and `CorrectsTimesheetId`. `ILockable`: once consumed it is Locked and the interceptor refuses writes |
| **TimeEntries** | One row per date, unique on (`TimesheetId`,`WorkDate`). Hours and days stored as scaled integers; project and site names snapshotted so a later rename cannot rewrite approved time |
| **TimeEntryOvertimeLines** | Hours per overtime category per day, unique on (`TimeEntryId`,`OvertimeCategoryCode`). A code, never a multiplier |
| **LeaveTypes** | `EntitlementDays` and `StatutoryEntitlementDays` are **nullable** — undetermined is not zero (ADR-032) — with their own verification status, source and compliance question |
| **LeaveEntitlements** | Per employee, leave type and leave year. Holds the granted days only; every movement is a transaction |
| **LeaveTransactions** | Append-only ledger: opening balance, accrual, taken, adjustment, forfeiture, encashment, reversal. The balance is derived from this, never stored |
| **LeaveRequests** | Dates, days, the calendar used, and `IsPaid` **frozen at request time** so reclassifying the type later cannot reprice leave already taken |
| **HolidayCalendars** | Company calendars with effective dates and one default at a time |
| **PublicHolidays** | Date, name, kind, `ActualDate` for an observed shift, and a verification status with a source — a public holiday is a claim about the law and is graded like one (ADR-033) |
| **EmployeeLoans** | Principal, interest, instalment terms, disbursement, the input lifecycle, and `AllowsOverRecovery` with its approver and reason. **No balance column** (ADR-034) |
| **LoanInstalments** | The schedule, tied to a payroll period where one is matched, with the run that recovered it |
| **LoanTransactions** | Append-only: disbursement, repayments, settlement, adjustment, write-off, reversal, each with reference and reversal metadata |
| **OvertimeRules** | TPT subtype of `StatutoryRules`. Category code, name, **nullable** multiplier, threshold hours, and the NSSA/tax treatment from spec §14 |
| **PayDivisorRules** | TPT subtype. Working days and ordinary hours per period, both nullable, for converting a salary to a daily or hourly rate (Q33) |
| **PayrollInputSnapshots** | The serialised snapshot each calculation ran on, with a SHA-256 hash and a seal timestamp. Unique on `PayrollRunEmployeeId` (ADR-035) |
| **PayrollRunInputSources** | Which approved inputs a run consumed, relationally, so "which runs used this timesheet?" is a query rather than a scan of every stored snapshot |

---

## 10. Accounting *(built — final release)*

`GlAccountMappings`: `Id`, `CompanyId`, `MappingType`, **`CurrencyCode`**, `AccountCode`,
`AccountName`, `CostCentre`, `IsActive`, `Notes`. Unique on
(`CompanyId`, `MappingType`, `CurrencyCode`) — one account per amount **per currency**, because a
USD wages account and a ZiG wages account are different accounts in every chart of accounts this
system will meet (ADR-036). The journal itself is derived from persisted payroll results and is
not stored.

`PayrollCostAllocations` also gained `ProjectSiteId` and `ProjectSiteName` in this release, so
labour cost reports by site as well as by project.

---

## 10a. Original Phase 3 accounting sketch *(superseded by §10)*

| Table | Key columns |
|---|---|
| **GlAccounts** | `Id`, `CompanyId`, `Code`, `Name`, `Type` (`Expense`/`Liability`/`Asset`/`Equity`/`Revenue`), `Currency`, `IsActive` |
| **GlMappings** | `Id`, `CompanyId`, `SourceType` (`EarningType`/`DeductionType`/`EmployerCostType`/`NetPay`/`BankAccount`), `SourceId`, `DebitAccountId`, `CreditAccountId`, `DepartmentId`, `ProjectId` |
| **JournalBatches** | `Id`, `CompanyId`, `PayrollRunId`, `BatchNumber`, `JournalDate`, `Currency`, `Description`, `Status` (`Draft`/`Exported`), `ExportedBy/At`, `ExportFormat`, `FilePath` |
| **JournalLines** | `Id`, `JournalBatchId`, `LineNumber`, `GlAccountId`, `Description`, `DebitAmount`, `CreditAmount`, `Currency`, `DepartmentId`, `ProjectId`, `ExchangeRateUsed` |

Target journal per run, per currency:

```
Dr  Salaries & Wages Expense        (gross earnings)
Dr  Employer NSSA Expense           (employer POBS)
Dr  APWCS Expense
Dr  ZIMDEF / SDF Levy Expense
    Cr  PAYE Payable                (PAYE + AIDS Levy)
    Cr  NSSA Payable                (employee + employer POBS)
    Cr  APWCS Payable
    Cr  Employee Deductions Payable (loans, medical aid, union, garnishments)
    Cr  Net Salaries Payable / Bank
```

Each `Cr …Payable` line reconciles directly to a `StatutoryObligations` row, so the payroll
liability in the accounts and the obligation register cannot drift apart.

---

## 11. Key relationships

```
Companies 1─* Employees 1─* EmployeeContracts (versioned, one IsCurrent)
                         1─1 EmployeeStatutoryProfiles
                         1─* EmployeeBankAccounts / RecurringEarnings / RecurringDeductions
                         1─* Loans 1─* LoanSchedules / LoanTransactions
Companies 1─* PayrollPeriods 1─* PayrollRuns 1─* PayrollRunEmployees
                                                  1─* PayrollEarningLines
                                                  1─* PayrollDeductionLines
                                                  1─* PayrollEmployerCostLines
                                                  1─* PayrollCalculationTraces
                                                  1─1 Payslips
PayrollRuns 1─* StatutoryObligations 1─* StatutoryPayments
                                     1─* StatutoryObligationLines ─* PayrollRunEmployees
TaxRules 1─* TaxBrackets
Currencies 1─* ExchangeRates
All tables ─▶ AuditLogs (by EntityName + EntityId)
```

## 12. Indexing and integrity notes

- Unique: `Employees(CompanyId, EmployeeNumber)`, `Employees(NationalId)` (filtered, non-null),
  `PayrollPeriods(CompanyId, CalendarId, Code)`, `Payslips(PayslipNumber)`,
  `StatutoryObligations(PayrollRunId, ObligationType, Currency)`.
- Covering indexes on `PayrollRunEmployees(PayrollRunId, EmployeeId)`,
  `AuditLogs(EntityName, EntityId, OccurredAt)`, `ExchangeRates(FromCurrency, ToCurrency, RateDate)`.
- `EffectiveFrom`/`EffectiveTo` on every rule table is indexed and validated for non-overlap per
  (rule type, currency) — overlapping active rules are a data error the system rejects on save.
- Foreign keys are enforced (`PRAGMA foreign_keys = ON` on SQLite).
- Deleting a payroll run is impossible once `Finalised`; correction is by supplementary or
  correction run, never by edit-in-place.
