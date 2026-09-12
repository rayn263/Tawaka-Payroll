# Tawaka Payroll — Architecture

**Document status:** Proposal, awaiting approval
**Last updated:** 2026-09-12
**Owner:** Architecture is jointly maintained. Update this document in the same commit as any
structural change.

---

## 1. Technology stack

### 1.1 Recommendation

| Layer | Choice | Reasoning |
|---|---|---|
| Language / runtime | **C# on .NET 8 (LTS)** | Long-term support to Nov 2026 with a clear path to .NET 10 LTS; first-class Windows desktop support; `decimal` is a native base-10 type, which matters for money; excellent tooling for the kind of unit testing a tax engine demands. |
| Desktop shell | **WPF host (net8.0-windows)** | Native Windows application, real installer, file system and printer access, no browser or server required. |
| User interface | **Blazor Hybrid inside the WPF shell (`BlazorWebView` / WebView2)** | Gives HTML + CSS layout power, so the interface can actually look like commercial accounting software rather than a default desktop form grid. Components are plain C#, share the domain model directly with no serialisation boundary, and can later be hosted by ASP.NET Core unchanged for the Phase 3 cloud/multi-company option. |
| Data access | **Entity Framework Core 8** | Migrations give us versioned schema evolution; the `SaveChanges` interceptor is the natural place to enforce the audit trail and period locking globally rather than per-screen. |
| Database (default) | **SQLite** | Single file, zero installation, trivially backed up and copied — correct for a single-site Zimbabwean SME. |
| Database (upgrade path) | **SQL Server Express / LocalDB, or PostgreSQL** | Selected by connection string plus provider switch. Required once multiple concurrent payroll officers or a server deployment are involved. All queries stay provider-agnostic (LINQ only, no raw SQL in the domain). |
| PDF generation | **QuestPDF** for statutory forms and registers; **WebView2 `PrintToPdfAsync`** for payslips | The payslip is a Razor component, so one template serves the on-screen preview, the printer and the PDF — they cannot drift apart. QuestPDF handles the fixed-layout statutory documents. **License note:** QuestPDF Community is free below a revenue threshold; confirm eligibility or budget for a licence (see `DECISIONS.md` ADR-009). |
| Excel export | **ClosedXML** (MIT) | Real `.xlsx`, not CSV renamed. |
| CSV export | **CsvHelper** (MS-PL/Apache-2.0) | Correct escaping and culture handling. |
| Validation | **FluentValidation** | Keeps rules out of the UI and testable. |
| Logging | **Serilog** (rolling file sink) | Diagnostic log. Distinct from the audit trail, which lives in the database. |
| Password hashing | **ASP.NET Core `PasswordHasher<T>`** (PBKDF2-HMAC-SHA256, 100k+ iterations) | Vetted implementation; no hand-rolled cryptography. |
| Testing | **xUnit + FluentAssertions**; golden-case fixtures for the tax engine | The calculation engine is pure and deterministic specifically so it can be exhaustively tested. |
| Installer | **Velopack** (or WiX/MSIX) | Signed installer plus delta updates, so statutory rate updates can be shipped quickly. |

### 1.2 Alternatives considered and rejected

- **Electron + React + Node** — a good-looking interface quickly, but it puts payroll money
  arithmetic in JavaScript, whose `number` type is IEEE-754 binary floating point. Every money
  value would need a decimal library and discipline. Rejected on correctness grounds.
- **Python + PySide6** — fast to build, weaker on Windows deployment, packaging and long-lived
  schema migration; a smaller pool of maintainers for a business system.
- **Web application from day one** — adds hosting, connectivity and uptime as hard dependencies.
  Zimbabwean SMEs need payroll to run when the internet does not. Chosen path is desktop first,
  with the UI layer deliberately portable to the web later.

### 1.3 Money representation (critical decision)

Money is never a bare `decimal` or `double` in this system. It is a value object:

```
Money { decimal Amount; CurrencyCode Currency; }
```

- All arithmetic is `decimal`-based, MidpointRounding.AwayFromZero at 2 decimal places unless a
  currency defines otherwise.
- Operations between different currencies throw. Conversion is explicit and always records the
  rate used.
- On SQLite, money persists as a scaled integer (minor units, `INTEGER`) plus a currency column,
  through an EF Core value converter. This avoids SQLite's habit of storing `decimal` as text,
  which breaks comparison, ordering and `SUM`. On SQL Server/PostgreSQL it persists as
  `DECIMAL(18,4)`.
- Rounding is applied once, at defined points in the pipeline, and every rounding adjustment is
  recorded in the calculation trace. Statutory totals are reconciled against the sum of rounded
  employee lines, never recomputed from unrounded values.

---

## 2. Application architecture

### 2.1 Layering

A single process, layered along Clean Architecture lines. Dependencies point inward only.

```
┌──────────────────────────────────────────────────────────────┐
│  Tawaka.Ui.Desktop            WPF shell + Blazor components   │
│  (screens, view models, navigation, printing, dialogs)        │
└───────────────────────────┬──────────────────────────────────┘
                            │ depends on
┌───────────────────────────▼──────────────────────────────────┐
│  Tawaka.Application       Use cases / services                │
│  (CreatePayrollRun, CalculatePayroll, ApprovePayroll,         │
│   RecordStatutoryPayment, GeneratePayslips, reports)          │
│  Defines interfaces: IRepository<>, IClock, IRuleResolver,    │
│  IExchangeRateProvider, ICurrentUser, IAuditWriter            │
└──────┬──────────────────────────────────┬────────────────────┘
       │                                  │
┌──────▼──────────────────┐   ┌───────────▼─────────────────────┐
│ Tawaka.Payroll.Engine   │   │ Tawaka.Domain                   │
│ Pure calculation.       │   │ Entities, value objects         │
│ NO database access.     │──▶│ (Money, CurrencyCode, DateRange)│
│ Input: snapshot         │   │ Enums, invariants               │
│ Output: result + trace  │   │ No external dependencies        │
└─────────────────────────┘   └─────────────────────────────────┘
                            ▲
┌───────────────────────────┴──────────────────────────────────┐
│  Tawaka.Infrastructure    EF Core, migrations, repositories,  │
│  audit interceptor, lock interceptor, PDF/Excel/CSV writers,  │
│  file storage, backup, seeding                                │
└──────────────────────────────────────────────────────────────┘
```

### 2.2 The five architectural rules

**Rule 1 — No statutory constant appears in code.**
There is no `const decimal AidsLevy = 0.03m`. Every statutory number is a row in a rule table
with an effective date range and a currency. The engine asks:

```csharp
var nssa = ruleResolver.Resolve<NssaRule>(currency, payDate, employeeContext);
```

If no rule exists for the date and currency, the engine **raises a blocking error**. It never
falls back to a default. A missing rule must be a visible configuration failure, not a silent
wrong answer.

**Rule 2 — Calculation is pure and deterministic.**
`Tawaka.Payroll.Engine` receives an immutable `PayrollInputSnapshot` (employee terms, earnings,
timesheet, loans, resolved rules, resolved exchange rates) and returns a `PayrollResult`. It
cannot read the database, the clock or the current user. The same snapshot always produces the
same result — which is what makes the engine testable and the audit trail meaningful.

**Rule 3 — Every run snapshots the rules it used.**
When a payroll run is calculated, the resolved rule set and exchange rates are persisted with the
run (both by foreign key and as an immutable JSON copy). Re-opening a September 2026 payroll in
2028 reproduces the September 2026 figures exactly, even if the rules have since been edited.

**Rule 4 — Every figure carries a trace.**
Each calculated amount records how it was derived: the rule id, the bracket applied, the inputs,
the intermediate values and the rounding. This is exposed in the UI as a "How was this
calculated?" drill-down on the payroll preview. It is the difference between a payroll system and
a black box.

**Rule 5 — Locking and auditing are enforced at the data layer.**
A `SaveChanges` interceptor writes the audit trail and rejects writes to entities belonging to a
locked payroll period. No screen, import or future feature can bypass it by forgetting a check.

### 2.3 Project/file structure

```
Tawaka-Payroll/
├── docs/                              # Living documentation (this folder)
├── src/
│   ├── Tawaka.Domain/
│   │   ├── Common/                    # Money, CurrencyCode, DateRange, Entity, AuditableEntity
│   │   ├── Employees/
│   │   ├── Payroll/
│   │   ├── Statutory/                 # Rule entities, obligation state machine
│   │   ├── Time/                      # Timesheets, leave
│   │   └── Enums/
│   ├── Tawaka.Payroll.Engine/
│   │   ├── Inputs/                    # PayrollInputSnapshot and friends
│   │   ├── Stages/                    # One class per pipeline stage (§5)
│   │   ├── Strategies/                # PAYE table strategies, currency strategies
│   │   ├── Results/                   # PayrollResult, CalculationTrace, Warning
│   │   └── PayrollCalculator.cs
│   ├── Tawaka.Application/
│   │   ├── Abstractions/              # Interfaces implemented by Infrastructure
│   │   ├── Employees/ Payroll/ Statutory/ Reports/ Settings/
│   │   └── Security/                  # Permission checks
│   ├── Tawaka.Infrastructure/
│   │   ├── Persistence/               # DbContext, configurations, migrations
│   │   ├── Interceptors/              # AuditInterceptor, PeriodLockInterceptor
│   │   ├── Repositories/
│   │   ├── Seeding/                   # Reference data + statutory rule seed (dated, sourced)
│   │   ├── Documents/                 # PDF, Excel, CSV
│   │   └── Backup/
│   ├── Tawaka.Ui.Desktop/
│   │   ├── App.xaml / MainWindow.xaml # WPF shell hosting BlazorWebView
│   │   ├── Components/
│   │   │   ├── Layout/                # Sidebar, topbar, breadcrumbs
│   │   │   ├── Shared/                # DataGrid, StatusBadge, MoneyText, ConfirmDialog
│   │   │   ├── Pages/                 # One folder per navigation node (§4)
│   │   │   └── Payslip/               # Payslip Razor template (screen + print + PDF)
│   │   └── wwwroot/                   # CSS design tokens, fonts, logo assets
│   └── Tawaka.Reporting/              # Report definitions and renderers
└── tests/
    ├── Tawaka.Payroll.Engine.Tests/   # Golden cases — the most important test project
    ├── Tawaka.Application.Tests/
    └── Tawaka.Infrastructure.Tests/   # Migration, audit and lock enforcement tests
```

### 2.4 Naming stability contract

Once created, the following are **frozen** and may only change through an ADR in
`docs/DECISIONS.md`: project names, database table names, column names, domain entity names,
enum member names and the public engine interfaces (`PayrollInputSnapshot`, `PayrollResult`,
`IStatutoryRuleResolver`). Additive change is always preferred to renaming.

---

## 3. Navigation and menu structure

Fixed left sidebar, collapsible, with a top bar carrying the company name, active payroll period,
current user and a global search. Permission-trimmed: a user never sees a node they cannot open.

```
DASHBOARD

EMPLOYEES
  ├─ All Employees            (search, filter, sort, bulk actions)
  ├─ Add Employee
  ├─ Departments
  ├─ Job Titles
  └─ Projects & Sites

PAYROLL
  ├─ Payroll Periods          (create monthly / weekly / fortnightly / custom)
  ├─ Payroll Runs             (the 12-step workflow, §6)
  ├─ Calculation Preview      (full per-employee grid before approval)
  ├─ Approvals                (queue for authorised approvers)
  └─ Payslips                 (generate, view, print, re-issue)

TIME & ATTENDANCE            [Phase 2]
  ├─ Timesheets
  ├─ Leave Requests
  ├─ Leave Balances
  └─ Public Holidays

LOANS & ADVANCES             [Phase 2]
  ├─ Loans
  ├─ Advances
  └─ Repayment Schedules

STATUTORY
  ├─ Obligations Register     (calculated / deducted / approved / paid, per period per currency)
  ├─ Record Payment
  ├─ Outstanding Obligations
  └─ Statutory Returns        (P2, P4, ITF16 and other return exports)

REPORTS
  ├─ Payroll Reports
  ├─ Tax Reports
  ├─ NSSA Reports
  ├─ Management Reports
  ├─ Payment Reports
  └─ Accounting Export        [Phase 3]

SETTINGS
  ├─ Company Profile
  ├─ Payroll Settings
  ├─ Currencies & Exchange Rates
  ├─ Tax Configuration        (PAYE tables, credits, exemptions — versioned)
  ├─ NSSA Configuration       (POBS rates, ceiling, eligibility — versioned)
  ├─ APWCS Configuration      (industry code, rate — versioned)
  ├─ Other Levies             (ZIMDEF, SDF, NEC — versioned)
  ├─ Earning Types
  ├─ Deduction Types
  ├─ Leave Types
  ├─ Payslip Settings
  ├─ Users & Roles
  └─ Audit Log
```

---

## 4. Dashboard

Currency totals are **never** added across currencies. The dashboard shows a USD column and a ZiG
column side by side. A consolidated figure is available only on explicit request, and then always
displays the rate, the rate date and both original totals.

**Counters:** total employees, active, permanent, contract, casual, project-based.

**Current payroll (per currency):** gross payroll, total employee deductions, total PAYE, total
AIDS Levy, total NSSA employee, total employer contributions, total net payroll, status badge.

**Warning cards** (each links to the screen that resolves it):

| Warning | Trigger |
|---|---|
| ⚠ Payroll awaiting approval | A run is in `AwaitingReview` |
| ⚠ PAYE payment pending | Obligation approved but not paid, or due date within 3 days |
| ⚠ NSSA payment pending | As above for POBS/APWCS |
| ⚠ Statutory payment overdue | Past the configured due date and unpaid |
| ⚠ Employee missing tax information | Active employee with no TIN/BP number |
| ⚠ Employee missing NSSA information | Active, NSSA-eligible, no NSSA number |
| ⚠ Employee missing bank details | Active, payment method = bank transfer, no account |
| ⚠ Contract expiring soon | Contract end date within the configured window (default 30 days) |
| ⚠ Exchange rate missing/stale | Mixed-currency payroll and no rate for the pay date |
| ⚠ Statutory rule expiring | A tax/NSSA rule's `effective_to` falls before the next pay date |

The last card matters: it turns "the tax tables are out of date" from a silent wrong answer into a
visible task.

---

## 5. Payroll calculation engine

### 5.1 Pipeline

`PayrollCalculator` runs ordered stages. Each stage is a separate class, receives the accumulating
`PayrollContext`, and appends to the trace. Stages are individually unit tested.

| # | Stage | What it does |
|---|---|---|
| 0 | **ResolveContext** | Loads the employee's effective contract, employment type, payroll currency, eligibility flags and the pay date. Determines which rule versions apply. Fails loudly if any required rule is missing. |
| 1 | **GatherInputs** | Recurring earnings/deductions, once-off entries, approved timesheet quantities, loan instalments due, leave affecting pay. |
| 2 | **ComputeBasicEarnings** | Applies the earnings basis for the employment type: monthly salary, weekly, daily × days, hourly × hours, project/contract amount, commission. |
| 3 | **ComputeAdditionalEarnings** | Overtime (rule-driven multipliers), Sunday/public holiday/night shift, bonuses, all allowances. Each earning line carries its **EarningType flags**: `IsTaxable`, `IsNssaApplicable`, `IsIncludedInGross`, `IsPensionable`, `IsEmployerLevyBase`, `IsExemptUpToLimit`. |
| 4 | **ApplyExemptions** | Applies configured exemption rules (for example an annual bonus exemption limit) to the flagged lines, recording exempt and taxable portions separately. |
| 5 | **ComputeGross** | Sums lines flagged `IsIncludedInGross`, per currency. |
| 6 | **ComputeNssa** | Insurable earnings = sum of `IsNssaApplicable` lines, capped at the configured ceiling for the currency and date. Applies eligibility rules (employment type, age bounds). Produces employee and employer POBS amounts. |
| 7 | **ComputePreTaxDeductions** | Deductions flagged as reducing taxable income (for example the NSSA employee contribution and approved pension contributions), each subject to its own configured limit. |
| 8 | **DetermineTaxBase** | Applies the configured **multi-currency strategy** (§5.3) to produce the taxable amount(s) and the currency in which the tax table is applied. Records the exchange rate, its source and its date. |
| 9 | **ComputePaye** | Applies the versioned progressive bracket table for the tax base currency and date, using the configured period basis (per-period table or cumulative/annual-equivalent). |
| 10 | **ApplyTaxCredits** | Applies entitled credits (elderly, disabled, blind, medical aid/expenses) subject to their configured caps. Credits cannot reduce tax below zero. |
| 11 | **ComputeAidsLevy** | Applies the versioned AIDS Levy rate to the configured base (default: tax after credits — see risk R-04). |
| 12 | **ApportionTaxByCurrency** | Where the strategy aggregated currencies, splits the resulting tax back across the source currencies pro rata and records the split, so each currency is remitted to the correct ZIMRA account. |
| 13 | **ComputePostTaxDeductions** | Loans, advances, union dues, medical aid (if configured post-tax), garnishments, staff purchases, accommodation. Applies ordering by configured priority and the minimum-net-pay floor. |
| 14 | **ComputeEmployerCosts** | NSSA employer POBS, APWCS, ZIMDEF, Standards Development Fund, NEC employer portion, employer pension/medical — each from its own versioned rule. These never reduce net pay. |
| 15 | **RoundAndFinalise** | Applies rounding policy, computes net pay, reconciles totals, produces `PayrollResult`. |
| 16 | **Validate** | Produces warnings and blocking errors (negative net pay, missing statutory identifiers, net below minimum, ceiling exceeded, stale rate). Blocking errors prevent approval; warnings do not. |

### 5.2 Worked illustration (USD, monthly)

This shows the shape of the output, using the seed rates recorded in
`COMPLIANCE_ZIMBABWE.md`. **The figures below are illustrative and depend on unverified seed
configuration.**

```
Basic salary                     USD  850.00   taxable, NSSA-able
Housing allowance                USD  100.00   taxable, NSSA-able (configurable)
Transport allowance              USD   50.00   taxable
Overtime (10h @ 1.5)             USD   75.00   taxable
                                 ─────────────
Gross earnings                   USD 1,075.00

NSSA insurable earnings   min(950.00, ceiling 700.00) = 700.00
NSSA employee 4.5% × 700.00      USD   31.50   (allowable deduction)

Taxable income  1,075.00 − 31.50 USD 1,043.50

PAYE (monthly USD brackets)
    0.00 –   100.00  @  0%   =     0.00
  100.01 –   300.00  @ 20%   =    40.00
  300.01 – 1,043.50  @ 25%   =   185.88
                                 ─────────
PAYE before credits              USD  225.88
Tax credits                      USD    0.00
PAYE after credits               USD  225.88
AIDS Levy 3% × 225.88            USD    6.78

Deductions: NSSA 31.50 + PAYE 225.88 + AIDS Levy 6.78 = USD 264.16
NET PAY                          USD  810.84

Employer costs
  NSSA employer 4.5% × 700.00    USD   31.50
  APWCS (industry rate × base)   USD    xx.xx
  ZIMDEF 1% of leviable wage bill USD   xx.xx
  Standards Development Fund      USD   xx.xx
                                 ─────────
Total employer cost = Gross + employer costs
```

Every line above is drillable to its trace: rule id, effective date, bracket, inputs.

### 5.3 Multi-currency tax strategy (configurable, never invented)

Zimbabwe guidance on employees remunerated in more than one currency is the single largest
compliance uncertainty in this project (risk R-01). The engine therefore treats the methodology as
**a named, versioned, configurable strategy recorded on every calculation**, rather than baking one
interpretation into code.

| Strategy | Behaviour | When it applies |
|---|---|---|
| `SingleCurrency` | Employee is paid wholly in one currency; that currency's tax table applies directly. | Default for most employees. |
| `AggregateInPrimaryCurrency` | Secondary-currency earnings are converted to the primary currency at the rule-defined rate, aggregated, taxed once on the primary currency's table, then the resulting tax is apportioned back pro rata by source currency for remittance. | Proposed default for mixed-currency employees. Prevents the tax-free threshold being claimed twice. **Requires sign-off.** |
| `SeparatePerCurrency` | Each currency stream taxed independently on its own table. | Available where an advisor confirms it, or for comparison. |

Whichever applies, the run records: strategy name and version, rate used, rate direction, rate
source, rate date, original amounts, converted amounts, tax computed, tax apportioned per
currency. Nothing is overwritten.

The exchange rate used is resolved by a configurable **rate determination rule** (rate on pay date
/ rate on period end / rate on date of payment / manually pinned rate), stored on the run, and
frozen once the run is calculated. Editing today's rate never changes a past payroll.

---

## 6. Payroll workflow and run states

```
Draft ──▶ Calculating ──▶ AwaitingReview ──▶ Approved ──▶ Finalised ──▶ Paid ──▶ Locked
  ▲            │                │                │
  └────────────┴── recalculate ─┴── reject ──────┘
                                                          Locked ──▶ Reopened (Administrator only,
                                                                     reason required, fully audited)
```

| Step | Action | Permission | Effect |
|---|---|---|---|
| 1 | Create payroll run for a period | `Payroll.Create` | Draft |
| 2 | Select employees | `Payroll.Edit` | Snapshot of employee terms taken |
| 3 | Enter/confirm variable earnings, pull approved timesheets | `Payroll.Edit` | |
| 4 | Calculate | `Payroll.Calculate` | Rules and rates resolved and snapshotted |
| 5 | Review preview grid (§7) | `Payroll.View` | |
| 6 | Warnings and blocking errors shown | — | Blocking errors prevent step 7 |
| 7 | Approve | `Payroll.Approve` (must differ from calculator if segregation of duties is on) | Approved; figures frozen |
| 8 | Finalise | `Payroll.Finalise` | Payslip numbers issued; obligations created as CALCULATED + DEDUCTED |
| 9 | Generate payslips | `Payslip.Generate` | PDFs written and hashed |
| 10 | Record net-pay payment | `Payroll.RecordPayment` | Status Paid; bank file [Phase 3] |
| 11 | Record statutory payments | `Statutory.RecordPayment` | Obligations move APPROVED → PAID individually |
| 12 | Lock | `Payroll.Lock` | Data-layer immutability |

**Reopening** requires the `Payroll.Reopen` permission, a typed reason, and produces a permanent
audit entry. Any figure that changes on recalculation is diffed against the locked version and the
difference is reported before the change is committed.

---

## 7. Payroll calculation preview

The pre-approval grid, one row per employee, with currency-grouped subtotals and a grand total per
currency (never across currencies):

| Employee | Type | Cur | Basic | Allowances | Overtime | Bonus | **Gross** | Taxable | PAYE | AIDS Levy | NSSA Emp | Other Ded | **Net Pay** | NSSA Empr | APWCS | Other Empr | **Total Cost** |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|

Features: freeze first column, per-column totals, export to Excel, click any figure to open the
calculation trace, filter to "employees with warnings only", and a variance column comparing
against the previous period (flagging anything outside a configured tolerance — the fastest way to
catch a data-entry error before payment).

---

## 8. Payslip design

One Razor template renders the on-screen payslip, the printed page and the PDF, so they can never
diverge. A4 portrait, professional typography, monochrome-safe.

```
┌──────────────────────────────────────────────────────────────────────────┐
│ [LOGO]   TAWAKA CONSTRUCTION (PVT) LTD          PAYSLIP                   │
│          123 Samora Machel Ave, Harare          Payslip No: PS-2026-09-0042│
│          +263 …  |  payroll@…                   Period: 01–30 Sep 2026    │
│          BP/TIN: …   NSSA Employer No: …        Payment date: 30 Sep 2026 │
│                                                 CURRENCY: USD             │
├──────────────────────────────────────────────────────────────────────────┤
│ Employee: John Moyo               Employee No: EMP-0031                   │
│ National ID: 63-1234567 X 42      Job title: Site Foreman                 │
│ Department: Operations            Employment type: Permanent              │
│ Project/Site: Nyanga Shop Renov.  Engaged: 04 Mar 2021                    │
│ Bank: … Acc ****4821              Payment method: Bank transfer           │
├───────────────────────────────────┬──────────────────────────────────────┤
│ EARNINGS                    Amount│ DEDUCTIONS                     Amount│
│ Basic Salary            USD 850.00│ PAYE                       USD 225.88│
│ Housing Allowance       USD 100.00│ AIDS Levy                  USD   6.78│
│ Transport Allowance     USD  50.00│ NSSA Employee (POBS)       USD  31.50│
│ Overtime (10.0 hrs)     USD  75.00│ Medical Aid                USD   0.00│
│ Bonus                   USD   0.00│ Pension                    USD   0.00│
│                                   │ Loan Repayment             USD   0.00│
│                                   │ Salary Advance             USD   0.00│
│                                   │ Union / NEC Dues           USD   0.00│
│                                   │ Other Deductions           USD   0.00│
│ ─────────────────────────────────  ───────────────────────────────────── │
│ GROSS EARNINGS       USD 1,075.00 │ TOTAL DEDUCTIONS           USD 264.16│
├───────────────────────────────────┴──────────────────────────────────────┤
│                                                   NET PAY     USD 810.84 │
├──────────────────────────────────────────────────────────────────────────┤
│ EMPLOYER CONTRIBUTIONS (not deducted from the employee)                   │
│ NSSA Employer (POBS)  USD 31.50 │ APWCS  USD xx.xx │ ZIMDEF  USD xx.xx    │
├──────────────────────────────────────────────────────────────────────────┤
│ STATUTORY STATUS — this payslip period                                    │
│ PAYE        Calculated USD 225.88 │ Deducted ✔ │ Approved ✔ │ Remitted ✘  │
│ AIDS Levy   Calculated USD   6.78 │ Deducted ✔ │ Approved ✔ │ Remitted ✘  │
│ NSSA Empl.  Calculated USD  31.50 │ Deducted ✔ │ Approved ✔ │ Remitted ✘  │
├──────────────────────────────────────────────────────────────────────────┤
│ YEAR TO DATE (USD)  Gross 9,675.00 │ PAYE 2,032.92 │ NSSA 283.50 │ Net …  │
├──────────────────────────────────────────────────────────────────────────┤
│ Leave: Annual taken 4.0 / balance 8.0   Sick taken 0.0                    │
│ Loan balance: USD 0.00                                                    │
├──────────────────────────────────────────────────────────────────────────┤
│ Prepared by: … │ Approved by: … │ [signature]  │ Footer / disclaimer text │
└──────────────────────────────────────────────────────────────────────────┘
```

Payslip rules:

- **Configured statutory categories always appear, even at 0.00.** A zero is information; a
  missing line is ambiguity.
- Every amount is prefixed with its currency code. A normal payslip shows one currency. Where an
  employee genuinely receives mixed-currency earnings, the payslip shows the original currency and
  amount of each line, with a clearly labelled conversion block showing the rate, rate date and
  rate source — the original is never replaced by the converted figure.
- The **statutory status block** shows remittance status but attaches it to the employer's
  obligation, never asserting payment that has not been recorded.
- Employer contributions are visually separated and explicitly labelled as not deducted from the
  employee.
- Each payslip stores a content hash; re-issuing a payslip after a reopen produces a new version
  with a visible revision marker, and the superseded version is retained.

---

## 9. Security model

### 9.1 Roles and permissions

Permissions are granular and assigned to roles; roles are assigned to users. Custom roles are
supported. Default roles:

| Permission | Administrator | Payroll Officer | Manager | Viewer |
|---|:--:|:--:|:--:|:--:|
| View employees | ✔ | ✔ | ✔ | ✔ |
| Create/edit employees | ✔ | ✔ | — | — |
| View/change salary and rates | ✔ | ✔ | view | — |
| Create/calculate payroll | ✔ | ✔ | — | — |
| View payroll preview | ✔ | ✔ | ✔ | ✔ |
| **Approve payroll** | ✔ | — | ✔ | — |
| Finalise payroll | ✔ | ✔ | — | — |
| Generate payslips | ✔ | ✔ | — | — |
| Record net-pay payment | ✔ | ✔ | — | — |
| Record statutory payment | ✔ | ✔ | — | — |
| Lock payroll | ✔ | ✔ | — | — |
| **Reopen locked payroll** | ✔ | — | — | — |
| **Edit tax / NSSA / levy rules** | ✔ | — | — | — |
| Manage users and roles | ✔ | — | — | — |
| View audit log | ✔ | — | view own | — |
| Run reports | ✔ | ✔ | ✔ | ✔ |
| Export data | ✔ | ✔ | ✔ | — |

**Segregation of duties:** by default the user who calculated a run cannot approve it. This is a
company setting, and disabling it is itself an audited event.

### 9.2 Other controls

- Password policy (length, complexity, expiry, history) configurable; PBKDF2 hashing; account
  lockout after configurable failed attempts.
- Idle session lock with re-authentication.
- Optional database encryption at rest (SQLCipher) for the employee data file.
- Sensitive fields (National ID, bank account, salary) masked for roles without the permission,
  and every view of them is audit-logged where the company enables strict mode.
- Audit log is append-only: no UPDATE or DELETE path exists in the application, and the table has
  no application-level delete permission.
- Backups are configurable, scheduled and verified; restore is an administrator action requiring
  confirmation.
- Zimbabwe's Cyber and Data Protection Act obligations (lawful processing, security, retention)
  are addressed by role-based access, encryption at rest, audit logging and configurable
  retention. Legal review required before go-live — see risk R-10.

---

## 10. Feature status

### Completed
- Nothing. This repository contains documentation only.

### In progress
- Architecture and compliance research (this document set), awaiting approval.

### Pending — Phase 1
Company setup · employee management · employment types · salary setup · payroll periods ·
earning types · deduction types · PAYE engine · AIDS Levy · NSSA POBS · APWCS · dual-currency
core · payslips · core payroll reports · database and migrations · settings · users and roles.

### Pending — Phase 2
Timesheets · projects and sites · loans · advances · leave · statutory payment tracking screens ·
full audit log UI · granular permissions.

### Pending — Phase 3
Accounting/journal export · advanced reporting · bank payment files · multi-company · cloud
backup · advanced statutory returns.

### Known bugs
- None (no code yet).

### Next development step
Architecture approved in principle (2026-09-12). Compliance research delivered as
`ZIMBABWE_PAYROLL_COMPLIANCE_SPEC_V1.md`, which is now the authoritative specification for the
calculation engine and adds ADR-012 (verification gate), ADR-013 (official period tables only) and
ADR-014 (no legal employment determinations). Awaiting approval of that specification, then
implement Phase 0/1 Milestone 1: solution skeleton, `Money`/`CurrencyCode` value
objects, EF Core context with audit and lock interceptors, and the statutory rule tables with
seed data and verification metadata.
