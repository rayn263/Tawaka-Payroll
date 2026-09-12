# Project State

**Last updated:** 2026-09-12
**Current phase:** Phase 0 / Phase 1 foundation
**Current milestone:** Milestone 2 — Company, employees and security — **COMPLETE, awaiting approval**
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
| Statutory rules seeded | **24** |
| Verified (usable in live payroll) | **0** |
| Supported (blocked from live payroll) | 10 |
| Unverified (blocked from live payroll) | 14 |
| Blocking questions open | 14 (Q1, Q3-values, Q4a, Q5-partial, Q6, Q21–Q28) |

No rule is Verified because no primary source could be opened from the build environment; see
`ZIMBABWE_PAYROLL_COMPLIANCE_SPEC_V1.md` §0. The path to clearing the gate is §26 of that
document — ten documents, roughly one working day.

## 2. Architecture as built

Clean architecture, dependencies inward only. Eleven projects:

| Project | Purpose | Builds on Linux |
|---|---|---|
| `src/Tawaka.Domain` | Entities and value objects: `Money`, `CurrencyCode`, `DateRange`, currencies, statutory rules, audit, company, organisation, employees, earnings, security | yes |
| `src/Tawaka.Payroll.Engine` | Calculation tracing and currency conversion records. **No calculations yet** | yes |
| `src/Tawaka.Application` | Abstractions, rule resolution, live payroll gate, validation, authentication, employee and contract services | yes |
| `src/Tawaka.Infrastructure` | EF Core context, configurations, migrations, interceptors, seeding, DI | yes |
| `src/Tawaka.Ui.Shared` | **All Razor screens** (ADR-019) — layout, login, dashboard, employees, profile, projects, statutory, settings | yes |
| `src/Tawaka.Ui.Desktop` | WPF host only: window, `BlazorWebView`, startup wiring (`net8.0-windows`) | **no — Windows only** |
| `tools/Tawaka.Foundation.Cli` | Cross-platform foundation check: migrate, seed, print rule register, run the gate | yes |
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
| Migrations | `20260912221408_InitialFoundation`, `20260912224100_CompanyEmployeesAndSecurity` |
| Tables | 44 |

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

**Company-ready:** every company-scoped table carries `CompanyId`, and `StatutoryRule` carries a
nullable `CompanyId` so employer-specific rules (APWCS, NEC) can coexist with national ones. The
first release operates with exactly one company and no multi-company user interface.

Statutory rules use **table-per-type** mapping: shared identity, dating and verification metadata
in `StatutoryRules`, each rule kind in its own table. All monetary values and rates persist as
**scaled integers** (money 4dp, rates 8dp), because SQLite stores `decimal` as TEXT and would
break ordering and summation.

## 4. Rules implemented

Rule *infrastructure* is complete; no statutory *calculation* exists yet, by design.

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

## 5. Tests

| Project | Passing | Skipped |
|---|---|---|
| Domain | 29 | 0 |
| Application | 43 | 0 |
| Payroll.Engine | 31 | 31 |
| Infrastructure | 92 | 0 |
| **Total** | **195** | **31** |

The 31 skipped are compliance cases that need later milestones; they are present and counted
rather than omitted. See `TESTING.md`.

## 6. Known issues and limitations

1. **The Razor screens compile but have not been run.** All UI code now builds and is
   type-checked on every build (ADR-019), which is a real improvement on Milestone 1, but no
   screen has been rendered or clicked. Behaviour — data binding, navigation, form round-trips —
   is unverified until it runs on Windows. Component tests are proposed for Milestone 3.
2. **The WPF host is still Windows-only and uncompiled here**, as is
   `Microsoft.AspNetCore.Components.WebView.Wpf` 8.0.100. The host is now thin — a window, a
   WebView and startup wiring — so the surface that could break is small, but confirm the package
   version on the first Windows build.
3. **`Money` is still not persisted as a column pair.** Contract rates and recurring amounts store
   a decimal plus a currency code on the same row, which is equivalent but not the `Money` value
   object. The mapping lands with payroll transactions in Milestone 3.
4. **Employee documents have no upload flow.** The entity, table and profile tab exist; attaching
   a file is a Milestone 5 task.
5. **Recurring earnings and deductions have no editing screen yet.** They are modelled, persisted,
   validated and shown on the employee profile, but are captured programmatically until the
   payroll engine gives them a purpose (Milestone 3).
6. **The lock interceptor keys on a `Status` property** named `Locked`. When payroll transaction
   entities arrive they must be scoped to their period explicitly; that generalisation is
   Milestone 4 work and is noted in the interceptor.
7. **`NationalId` format checking is opt-in and currently off.** District codes and check letters
   vary, and rejecting a genuine identity number is worse than accepting an unusual one.

## 7. Blocking questions

Full register in `ZIMBABWE_PAYROLL_COMPLIANCE_SPEC_V1.md` §25; summary in
`docs/OPEN_QUESTIONS.md`. Highest impact: **Q26** (PAYE fixed-deduction column — blocks all PAYE),
**Q1** (multi-currency methodology), **Q22** (NSSA ceiling on weekly payroll), **Q6** (APWCS rate).

## 8. Next milestone

**Milestone 3 — Payroll periods and the calculation engine.** Not started; awaiting approval.

Planned: payroll calendars and periods (monthly, weekly, fortnightly, custom); the
`PayrollInputSnapshot` assembled from employee, contract, recurring earnings and deductions;
the ordered calculation pipeline consuming resolved statutory rules; `Money` persisted as a
column pair on payroll transaction lines; the calculation trace written per figure; and the
remaining compliance test cases turned on as the engine makes them executable.

Live payroll stays blocked throughout: the engine will calculate in development mode only until
the verification checklist is cleared.
