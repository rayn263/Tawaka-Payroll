# Project State

**Last updated:** 2026-09-12
**Current phase:** Phase 0 / Phase 1 foundation
**Current milestone:** Milestone 1 — Foundation — **COMPLETE, awaiting approval**
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

Clean architecture, dependencies inward only. Nine projects:

| Project | Purpose | Builds on Linux |
|---|---|---|
| `src/Tawaka.Domain` | Entities and value objects: `Money`, `CurrencyCode`, `DateRange`, currencies, statutory rules, audit, payroll period | yes |
| `src/Tawaka.Payroll.Engine` | Calculation tracing and currency conversion records. **No calculations yet** | yes |
| `src/Tawaka.Application` | Abstractions (`IClock`, `ICurrentUser`, `IStatutoryRuleSource`), rule resolution, live payroll gate | yes |
| `src/Tawaka.Infrastructure` | EF Core context, configurations, migrations, interceptors, seeding, DI | yes |
| `src/Tawaka.Ui.Desktop` | WPF shell hosting Blazor components (`net8.0-windows`) | **no — Windows only** |
| `tools/Tawaka.Foundation.Cli` | Cross-platform foundation check: migrate, seed, print rule register, run the gate | yes |
| `tests/Tawaka.Domain.Tests` | Money, currency, date range, conversion provenance | yes |
| `tests/Tawaka.Payroll.Engine.Tests` | Rule resolution, live gate, tracing, the 34-case compliance catalogue | yes |
| `tests/Tawaka.Infrastructure.Tests` | Migration, seeding, audit trail, period locking | yes |

Two solution files: `Tawaka.Payroll.Core.sln` (cross-platform, used by `build.sh` and `test.sh`)
and `Tawaka.Payroll.sln` (adds the Windows desktop shell).

## 3. Database

| | |
|---|---|
| Provider | SQLite |
| Migration | `20260912221408_InitialFoundation` |
| Tables | 16 |

`AidsLevyRules`, `AppSettings`, `ApwcsRules`, `AuditLogs`, `Currencies`,
`CurrencyTaxStrategyRules`, `EmployerLevyRules`, `ExchangeRates`, `NssaEligibilityRules`,
`NssaRules`, `PayrollPeriods`, `StatutoryRules`, `TaxBrackets`, `TaxCreditRules`,
`TaxExemptionRules`, `TaxRules`.

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
| Payroll.Engine | 31 | 31 |
| Infrastructure | 31 | 0 |
| **Total** | **91** | **31** |

The 31 skipped are compliance cases that need later milestones; they are present and counted
rather than omitted. See `TESTING.md`.

## 6. Known issues and limitations

1. **The desktop shell has never been compiled.** It targets `net8.0-windows`; this build
   environment is Linux and the WindowsDesktop SDK is unavailable. The C#, XAML and Razor are
   written but unverified. First Windows build may need fixes — treat it as untested code.
2. **`Microsoft.AspNetCore.Components.WebView.Wpf` version 8.0.100 is unpinned by testing.** It
   could not be restored here; confirm the current 8.x version on Windows.
3. **No authentication yet.** `ICurrentUser` resolves to `SystemUser`, which reports
   `SYSTEM (unauthenticated)` in the audit trail — deliberately conspicuous. Real users and
   permissions arrive in Milestone 2.
4. **No employee or payroll transaction model yet.** `PayrollPeriod` exists only as the lockable
   scope the interceptor enforces against.
5. **`Money` is not yet persisted.** Rule amounts carry currency at rule level, so the converter
   infrastructure is in place and tested but the `Money` column mapping lands with payroll
   transactions in Milestone 2.
6. **The lock interceptor keys on a `Status` property** named `Locked`. When payroll transaction
   entities arrive they must be scoped to their period explicitly; that generalisation is
   Milestone 4 work and is noted in the interceptor.

## 7. Blocking questions

Full register in `ZIMBABWE_PAYROLL_COMPLIANCE_SPEC_V1.md` §25; summary in
`docs/OPEN_QUESTIONS.md`. Highest impact: **Q26** (PAYE fixed-deduction column — blocks all PAYE),
**Q1** (multi-currency methodology), **Q22** (NSSA ceiling on weekly payroll), **Q6** (APWCS rate).

## 8. Next milestone

**Milestone 2 — Company, employees and contract versioning.** Not started; awaiting approval.

Planned: company profile and settings; departments, job titles, locations, projects and sites;
employee master with the profile tabs; **effective-dated `EmployeeContracts`** so a salary change
never overwrites history; employee statutory profile; bank and mobile-money accounts; recurring
earnings and deductions; users, roles and granular permissions with segregation of duties;
authentication replacing `SystemUser`.
