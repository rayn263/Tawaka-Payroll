# Project State

**Last updated:** 2026-09-15
**Current phase:** Phase 1 — core payroll
**Current milestone:** Milestone 5 — Time, attendance, leave and loans — **COMPLETE, awaiting approval**
**Payroll mode:** DEVELOPMENT (live payroll is gated and currently blocked — see below)

This file is the single place to look for where the project actually is. Update it in the same
commit as any change to structure, schema, rules or milestone status.

---

## 1. Live payroll status

```
LIVE PAYROLL BLOCKED
One or more statutory rules required for this payroll have not been verified.
```

| | |
|---|---|
| Statutory rules seeded | **28** |
| Verified (usable in live payroll) | **0** |
| Supported (blocked from live payroll) | 10 |
| Unverified (blocked from live payroll) | 18 |
| Blocking questions open | 19 (Q1, Q3-values, Q4a, Q5-partial, Q6, Q21–Q33) |

No rule is Verified because no primary source could be opened from the build environment; see
`ZIMBABWE_PAYROLL_COMPLIANCE_SPEC_V1.md` §0. The path to clearing the gate is §26 of that
document — ten documents, roughly one working day.

Access was **rechecked twice on 15 September 2026** — once for Milestone 4's ZWG table question and
again during Milestone 5. ZIMRA, NSSA and Veritas still return `403` at the CONNECT stage from the
egress proxy. Nothing was upgraded.

On **Q29** the two possible claims are kept apart, as instructed: *(a)* this environment cannot
retrieve an authoritative ZWG monthly table — true and evidenced; *(b)* no such table exists — **not
claimed**, and nothing in the codebase encodes it. See `ZIMBABWE_PAYROLL_COMPLIANCE_SPEC_V1.md`
§1.4a. Milestone 5 added **Q31** (overtime multipliers), **Q32** (statutory leave entitlements) and
**Q33** (the salary-to-daily-rate divisor).

## 2. Architecture as built

Clean architecture, dependencies inward only. Eleven projects:

| Project | Purpose | Builds on Linux |
|---|---|---|
| `src/Tawaka.Domain` | Entities and value objects: `Money`, `CurrencyCode`, `DateRange`, currencies, statutory rules, audit, company, organisation, employees, earnings, security | yes |
| `src/Tawaka.Payroll.Engine` | **The calculation engine**: pipeline, PAYE table, NSSA and **time/absence** calculators, rounding policy, input snapshot, immutable result, tracing | yes |
| `src/Tawaka.Application` | Abstractions, rule resolution, live payroll gate, validation, authentication, employee and contract services, **timesheet, leave, calendar and loan services**, payroll snapshot builder, **snapshot store** and run service | yes |
| `src/Tawaka.Infrastructure` | EF Core context, configurations, migrations, interceptors, seeding, DI | yes |
| `src/Tawaka.Ui.Shared` | **All Razor screens** (ADR-019) — layout, login, dashboard, employees, profile, projects, payroll runs and preview, payslip, statutory obligations, reports, statutory rules, **timesheets, leave, holiday calendar, loans, approval queue**, settings | yes |
| `src/Tawaka.Ui.Desktop` | WPF host only: window, `BlazorWebView`, startup wiring (`net8.0-windows`) | **no — Windows only** |
| `tools/Tawaka.Foundation.Cli` | Cross-platform check: migrate, seed, rule register, live gate, and `--payroll` to capture and approve a timesheet, leave and a loan, run a real calculation, print the approved inputs it consumed, the preview and an explanation, then walk the obligation lifecycle to a part payment | yes |
| `tests/Tawaka.Domain.Tests` | Money, currency, date range, conversion provenance | yes |
| `tests/Tawaka.Application.Tests` | Password hashing and policy, employee/contract/payment validation | yes |
| `tests/Tawaka.Payroll.Engine.Tests` | Rule resolution, live gate, tracing, the 34-case compliance catalogue | yes |
| `tests/Tawaka.Infrastructure.Tests` | Migration, seeding, audit, locking, security, employee lifecycle, contract versioning | yes |

Two solution files: `Tawaka.Payroll.Core.sln` (cross-platform, used by `build.sh` and `test.sh`)
and `Tawaka.Payroll.sln` (adds the Windows desktop host).

## 3. Database

| | |
|---|---|
| Provider | SQLite |
| Migrations | `20260912221408_InitialFoundation`, `20260912224100_CompanyEmployeesAndSecurity`, `20260913084801_PayrollRunsAndResults`, `20260915113432_StatutoryObligationsAndPayslips`, `20260915130007_TimeLeaveCalendarAndLoans` |
| Tables | 72 |

Milestone 1 (16): `AidsLevyRules`, `AppSettings`, `ApwcsRules`, `AuditLogs`, `Currencies`,
`CurrencyTaxStrategyRules`, `EmployerLevyRules`, `ExchangeRates`, `NssaEligibilityRules`,
`NssaRules`, `PayrollPeriods`, `StatutoryRules`, `TaxBrackets`, `TaxCreditRules`,
`TaxExemptionRules`, `TaxRules`.

Milestone 2 (28): `Companies`, `CompanyCurrencies`, `CompanyBankAccounts`, `Departments`,
`JobTitles`, `Locations`, `Clients`, `Projects`, `ProjectSites`, `EmploymentTypes`, `Employees`,
`EmployeeContracts`, `EmployeeStatutoryProfiles`, `EmployeePaymentAccounts`,
`EmployeeProjectAssignments`, `EmployeeStatusHistory`, `EmployeeNextOfKin`, `EmployeeDocuments`,
`EarningTypes`, `DeductionTypes`, `EmployeeRecurringEarnings`, `EmployeeRecurringDeductions`,
`Users`, `Roles`, `Permissions`, `RolePermissions`, `UserRoles`, `LoginAttempts`.

Milestone 3 (8): `PayrollRuns`, `PayrollRunEmployees`, `PayrollEarningLines`,
`PayrollDeductionLines`, `PayrollEmployerCostLines`, `PayrollCalculationTraces`,
`PayrollUnresolvedItems`, `PayrollCostAllocations`.

Milestone 4 (4): `Payslips`, `StatutoryObligations`, `StatutoryObligationLines`,
`StatutoryPayments`.

Milestone 5 (16): `Timesheets`, `TimeEntries`, `TimeEntryOvertimeLines`, `LeaveTypes`,
`LeaveEntitlements`, `LeaveTransactions`, `LeaveRequests`, `HolidayCalendars`, `PublicHolidays`,
`EmployeeLoans`, `LoanInstalments`, `LoanTransactions`, `OvertimeRules`, `PayDivisorRules`,
`PayrollInputSnapshots`, `PayrollRunInputSources`.

**Company-ready:** every company-scoped table carries `CompanyId`, and `StatutoryRule` carries a
nullable `CompanyId` so employer-specific rules (APWCS, NEC) can coexist with national ones. The
first release operates with exactly one company and no multi-company user interface.

Statutory rules use **table-per-type** mapping: shared identity, dating and verification metadata
in `StatutoryRules`, each rule kind in its own table. All monetary values and rates persist as
**scaled integers** (money 4dp, rates 8dp), because SQLite stores `decimal` as TEXT and would
break ordering and summation.

## 4. Rules implemented

Rule infrastructure and the calculation engine are complete. No rule is Verified, so every one of
these calculates in development mode only.

| Rule | Rule ID | Status | Note |
|---|---|---|---|
| PAYE USD monthly | `PAYE-USD-2026-MONTHLY` | Unverified | Bands seeded; official fixed-deduction column absent (Q26) |
| PAYE ZiG annual | `PAYE-ZWG-2026-ANNUAL` | Unverified | Independent bands; never derived from USD |
| AIDS Levy | `AIDS-LEVY-2026` | Supported | 3% of tax **after** credits — best-evidenced rule in the set |
| NSSA POBS | `NSSA-POBS-2026-USD` | Supported | 4.5%/4.5%, ceiling USD 700, basic only, 18-day casual test; ceiling application **NotDetermined** (Q22) |
| NSSA eligibility | `NSSA-ELIG-*` (12 rows) | Mixed | Types named in the evidence are Supported; inferred types Unverified |
| ZIMDEF | `ZIMDEF-2026` | Supported | 1% of leviable wage bill, due the **15th** |
| SDF | `SDF-2026` | Unverified, **inactive** | 0.5% has no statutory anchor in the evidence |
| Tax credits | `CREDIT-*` | Unverified | USD 75/month; medical credit **inactive with no percentage** (Q24 conflict) |
| Bonus exemption | `EXEMPT-BONUS-2026-USD` | Supported | USD 700, Finance Act No. 2 of 2024 |
| Multi-currency strategy | `CURRENCY-STRATEGY-2026` | Unverified | Aggregate-in-USD; needs advisor approval **and** a rate rule |
| APWCS | — | **not seeded** | Employer-specific; only NSSA can supply it (Q6) |
| Overtime | `OVERTIME-2026-OT_*` (3 rows) | Unverified | Weekday 1.5×, Sunday and public holiday 2.0×. Taxable, excluded from NSSA (§14). Rates are contractual or NEC (Q31) |
| Pay divisor | `PAY-DIVISOR-2026-MONTHLY` | Unverified | 22 working days, 176 ordinary hours. A convention, not an established rule (Q33) |
| Leave entitlements | `LeaveTypes` (6 rows) | Unverified, **null days** | Not a `StatutoryRule`: an entitlement lives on the leave type. Every one is undetermined (Q32) |
| Public holidays | — | **none seeded** | Set by Act and proclamation and moved yearly; captured with their source, never compiled in (ADR-033) |

## 5. Tests

| Project | Passing | Skipped |
|---|---|---|
| Domain | 29 | 0 |
| Application | 43 | 0 |
| Payroll.Engine | 230 | 1 |
| Infrastructure | 306 | 0 |
| **Total** | **608** | **1** |

Milestone 5 added 79 tests: timesheet capture and approval, period boundaries, overlapping leave,
overtime pricing and refusal, project allocation totals, loan schedules and balance reconciliation,
deduction limits, locked-input immutability, snapshot sealing and reproducibility, correction
isolation, and approved-input-only consumption.

Two compliance cases were activated against the real engine now that their inputs exist: **TC-20**
(advance recovered post-tax) and **TC-33** (the casual six-week engagement warning). One case
remains skipped — **TC-18**, part-time NSSA ceiling treatment, which depends on Q4a/Q22.

## 5a. What the engine does

Ordered pipeline: **derive approved inputs** → gather earnings → apply exemptions → gross → NSSA →
pre-tax deductions → tax base → PAYE → credits → AIDS Levy → post-tax deductions → net pay →
employer costs → cost allocation.

The first stage is Milestone 5's: it turns approved hours into basic pay for time-rated contracts,
prices each overtime category from its own dated rule, docks unpaid leave at the daily rate, and
places approved loan recoveries as post-tax deductions. Nothing outside the engine converts a
quantity into money (ADR-031). Every stage appends to a trace that names the rule, its verification grade, the
arithmetic, the rounding and the source.

No statutory value appears anywhere in the engine. Every rate, threshold and ceiling arrives
through resolved rules, and a rule that cannot be resolved produces an absent figure with a reason
— never a zero.

## 6. Known issues and limitations

1. **The Razor screens compile but have not been run.** All UI code now builds and is
   type-checked on every build (ADR-019), which is a real improvement on Milestone 1, but no
   screen has been rendered or clicked. Behaviour — data binding, navigation, form round-trips —
   is unverified until it runs on Windows. **This now covers the Milestone 5 screens too** —
   timesheets, leave, calendar, loans and the approval queue are written and type-checked, and no
   claim is made that their behaviour has been exercised. bUnit component tests are proposed for
   Milestone 6.
2. **The WPF host is still Windows-only and uncompiled here**, as is
   `Microsoft.AspNetCore.Components.WebView.Wpf` 8.0.100. The host is now thin — a window, a
   WebView and startup wiring — so the surface that could break is small, but confirm the package
   version on the first Windows build.
3. **`Money` is still not persisted as a column pair.** Contract rates and recurring amounts store
   a decimal plus a currency code on the same row, which is equivalent but not the `Money` value
   object. Loan amounts follow the same pattern. It is consistent and tested, but it is not the
   value object, and the conversion happens in the entity's accessors.
4. **Employee documents have no upload flow.** The entity, table and profile tab exist; attaching
   a file is still outstanding.
5. **Recurring earnings and deductions have no editing screen yet.** They are modelled, persisted,
   validated and shown on the employee profile, but are still captured programmatically.
6. **The lock interceptor now reaches payroll result rows** as well as `ILockable` entities: a
   locked run's per-employee results, lines, traces, unresolved items and cost allocations are
   refused at the data layer (ADR-030). Statutory obligations and payments are deliberately
   outside that guard, because settling an authority happens after the run is locked. Bulk
   operations that bypass the change tracker (`ExecuteDelete`, `ExecuteUpdate`) are not
   intercepted by EF Core and so are not covered; nothing in the codebase uses them against
   payroll data.
7. **`NationalId` format checking is opt-in and currently off.** District codes and check letters
   vary, and rejecting a genuine identity number is worse than accepting an unusual one.
8. **Month-to-date NSSA accumulation is not implemented.** If Q22 is answered as "accumulate within
   the calendar month", the engine refuses rather than approximating; pro-rata and per-run are
   implemented.
9. **Mixed-currency remuneration is modelled but not calculated.** The refusal path is implemented
   and tested; the aggregate-and-apportion arithmetic waits on Q1 being answered, because building
   it now would mean guessing the conversion direction.
10. **No bank payment file and no statutory submission file** — deliberately not built. Neither
    format could be obtained from an authoritative source, and inventing a layout would embed an
    unverified assumption in a file sent to a bank or an authority. The data for both is modelled
    and queryable, so they are a formatting exercise later, not a remodelling one.
11. **The payslip prints through the browser, not to PDF directly.** `window.print()` against a
    print stylesheet gives a correct A4 document and a PDF via the print dialogue. A direct
    PDF writer is still outstanding.
12. **Reports have no file export yet.** They render and print; CSV, Excel and PDF export follow
    with the accounting export in Milestone 6.
13. **Obligation due dates are derived from the configured rule, not from a verified calendar.**
    The 10th for ZIMRA and NSSA and the 15th for ZIMDEF come from the seeded rules, which are
    Supported at best. A due date shown as overdue should be checked against the authority's own
    calendar until the deadline rules are verified.
14. **`EarningType.DefaultMultiplier` is no longer read by the engine.** Overtime rates now live in
    dated, graded `OvertimeRules` (ADR-031), because a multiplier needs an effective date, a
    source, a verification status and a category, and a nullable column on an earning type carries
    none of those. The column is retained rather than dropped — renaming or removing an
    established column is not done casually — but it is a capture default only and must not be
    treated as authoritative.
15. **Actor fields store the user id, and the screens show it raw.** `SubmittedBy`, `ApprovedBy`
    and the rest hold the user's identifier, which renders as a GUID rather than a name. The
    attribution is correct and auditable; it is the display that is poor. Resolving ids to names
    at the point of display is a Milestone 6 task.
16. **Leave that crosses a payroll period is apportioned by calendar days.** A five-day request
    spanning a month end contributes the proportion of its days that fall inside the period. Where
    a request's days were captured against working days rather than calendar days, that
    apportionment is an approximation; splitting the request at capture time would be exact and is
    the better answer if it proves to matter.
17. **The casual engagement tally counts approved timesheet days.** An employee engaged without a
    timesheet does not accrue days towards the s.12(3) warning. For employment types that require
    a timesheet — which is every type carrying a threshold — this is complete; for any that do
    not, the warning would under-count.

## 7. Blocking questions

Full register in `ZIMBABWE_PAYROLL_COMPLIANCE_SPEC_V1.md` §25; summary in
`docs/OPEN_QUESTIONS.md`. Highest impact: **Q26** (PAYE fixed-deduction column — blocks all PAYE),
**Q1** (multi-currency methodology), **Q22** (NSSA ceiling on weekly payroll), **Q6** (APWCS rate).
All four remain open as at Milestone 5 and are restated in `docs/OPEN_QUESTIONS.md` so they cannot
drift out of sight, together with **Q29** and **Q30**. New this milestone: **Q31** (overtime
multipliers — contractual or NEC-mandated?), **Q32** (statutory leave entitlements) and **Q33** (the
working-days and ordinary-hours divisor).

## 8. Next milestone

**Milestone 6 — Accounting export, reporting depth and operational polish.** Not started; see the
Milestone 5 completion report for the proposed scope.

Live payroll stays blocked throughout: the engine calculates in development mode only until the
verification checklist in the compliance specification is cleared.
