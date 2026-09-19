# Tawaka Payroll

A professional, Windows-based payroll management system for businesses operating in Zimbabwe.

> **Status: release candidate — compliance unverified, Windows unvalidated.**
> The application supports the whole payroll journey: company setup, employees and effective-dated
> contracts, projects and sites, time and overtime, leave, loans and advances, input approval,
> calculation with a full audit trace, review, approval, finalisation, statutory obligations and
> payments, payslips, reports, an accounting journal, locking and audit.
>
> **It reports itself as COMPLIANCE-UNVERIFIED and will not run a live payroll.** No statutory
> figure in it has been read from ZIMRA, NSSA or a Statutory Instrument — every Zimbabwean official
> domain is blocked from the build environment. Clearing that is a finite, documented task for
> somebody with access to those documents: see `COMPLIANCE_SPEC.md` §26.
>
> **The Windows desktop host has never been compiled or run.** The screens compile and are
> type-checked on every build, and every one is rendered headlessly by `tests/Tawaka.Ui.Tests`;
> none has been rendered on Windows. See `docs/WINDOWS_VALIDATION.md`.

## Quick start

```bash
apt-get install -y dotnet-sdk-8.0   # or install the .NET 8 SDK for your platform
./build.sh                          # build the cross-platform solution
./test.sh                           # 695 passing, 1 skipped (pending Q4a/Q22)
./foundation-check.sh               # migrate, seed, print the rule register, run the live gate
dotnet run --project tools/Tawaka.Foundation.Cli -- --payroll                  # gate closed: calculation only
dotnet run --project tools/Tawaka.Foundation.Cli -- --payroll --verify-rules   # simulated verification: inputs, calculation, obligations
dotnet run --project tools/Tawaka.Foundation.Cli -- --demo-data                # seed the worked example
```

All screens live in `src/Tawaka.Ui.Shared` and compile anywhere. The Windows host builds from
`Tawaka.Payroll.sln` on Windows only.

## How do I…

Short answers. The full versions are in `DEPLOYMENT.md`, `USER_GUIDE.md` and `OPERATIONS.md`.

| | |
|---|---|
| **Install it** | Copy `Tawaka.Payroll.exe` anywhere the user can write and run it. Install Microsoft's WebView2 runtime first if the machine lacks it. No installer, no administrator rights |
| **Build the executable** | `dotnet publish src/Tawaka.Ui.Desktop -c Release -p:PublishProfile=win-x64`, on Windows |
| **Start for the first time** | It creates its database, migrates it, seeds the baseline and shows a generated administrator password **once**. Write it down; it cannot be recovered. Sign in as `admin` and change it |
| **Create the company** | Settings → Company profile, then Currencies, then Bank accounts |
| **Create users** | Administration → Users → Add a user, with a role each. **At least two are needed**: whoever calculates a payroll may not approve it |
| **Add employees** | Employees → Add employee captures the person and their first contract together. Afterwards their profile edits their details, terms, statutory numbers, standing earnings and deductions, and payment accounts |
| **Run payroll** | Payroll → create the period → New run → Calculate → review, using **Explain** on any figure |
| **Get it approved** | A different person approves it. Then finalise — which creates the statutory obligations — then record net wages paid |
| **Handle statutory obligations** | Statutory → Obligations. Approve one, then record a payment against it with a reference. Only a recorded payment marks it paid; part payments and reversals are supported |
| **Issue payslips** | From the payroll preview or the employee's payroll history. Rendered from the stored result, printed through the browser print dialogue |
| **Get figures out** | Reports — fourteen reports, filtered, per currency, exported to CSV, plus a balanced accounting journal per currency |
| **Back up** | Settings → Backup & restore, before every payroll run and every upgrade. Keep them off the machine |
| **Understand why LIVE payroll is blocked** | No statutory rule has been checked against its official source, so the application refuses to run a live payroll. Statutory → Compliance status says which questions are open |
| **Unblock it** | Obtain the documents, then verify each rule in Statutory → Statutory rules, recording what you read. `docs/COMPLIANCE_STATUS.md` lists exactly which documents are needed |

## What this is

Tawaka Payroll is designed as a production-grade payroll application — not a salary calculator.
It is built around four non-negotiable principles:

1. **Every statutory figure comes from a versioned, dated, configurable rule.**
   There are no tax rates, thresholds or ceilings hard-coded in the application. When
   legislation changes, an administrator adds a new rule version with an effective date.
   Historical payroll continues to use the rules that applied at the time.

2. **Original values are never destroyed.**
   Currency, amount, exchange rate, rate source and rate date are all preserved on every
   transaction. Conversions are additive, never destructive.

3. **The system never claims a statutory amount was paid when it was not.**
   PAYE, AIDS Levy, NSSA and every other obligation move through four independently
   recorded states: CALCULATED → DEDUCTED → APPROVED → PAID. Only a recorded payment with a
   reference, date and method sets PAID.

   The same discipline governs payroll inputs: a timesheet, a leave request or a loan reaches
   payroll only once somebody other than its author has approved it, and the run records exactly
   which approved records it read.

4. **Payroll data does not silently change.**
   Every create, update and approval is written to an append-only audit trail with the user,
   timestamp, old value and new value. Locked payroll periods are immutable at the data layer,
   not merely hidden in the user interface.

## Documentation

| Document | Contents |
|---|---|
| **Start here** | |
| [`DEPLOYMENT.md`](DEPLOYMENT.md) | Installing it, building it, publishing the Windows executable, and what the machine needs |
| [`USER_GUIDE.md`](USER_GUIDE.md) | Setting a company up and running its payroll, start to finish |
| [`OPERATIONS.md`](OPERATIONS.md) | Release stages, backups, restoring, locking, the audit trail, demonstration data |
| [`SECURITY.md`](SECURITY.md) | Authentication, authorisation, segregation of duties, integrity, and what is deliberately not attempted |
| **Where the project stands** | |
| [`PROJECT_STATE.md`](PROJECT_STATE.md) | Phase, architecture as built, database version, rules implemented and verified, known limitations |
| [`docs/FINAL_RELEASE_STATUS.md`](docs/FINAL_RELEASE_STATUS.md) | The release report: what was tested, what was found, what remains |
| [`docs/COMPLIANCE_STATUS.md`](docs/COMPLIANCE_STATUS.md) | Where every open compliance question stands, and what would answer it |
| [`docs/WINDOWS_VALIDATION.md`](docs/WINDOWS_VALIDATION.md) | The Windows validation that has **not** been performed, and exactly how to perform it |
| **Engineering** | |
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | Technology stack, layering, navigation, calculation pipeline, payslip design, file structure |
| [`docs/DATABASE_SCHEMA.md`](docs/DATABASE_SCHEMA.md) | Full relational schema, entity relationships, money and currency representation |
| [`docs/DECISIONS.md`](docs/DECISIONS.md) | The architecture decision record: every design decision, with its reasoning |
| [`TESTING.md`](TESTING.md) | How to run the tests, the behaviour/seed split, and the 34-case compliance catalogue |
| **Statutory** | |
| [`COMPLIANCE_SPEC.md`](COMPLIANCE_SPEC.md) | Authoritative specification for the calculation engine: rules with confidence grades, dual-currency methodology, 34 test cases, verification checklist |
| [`docs/COMPLIANCE_ZIMBABWE.md`](docs/COMPLIANCE_ZIMBABWE.md) | Zimbabwe statutory requirements, source references, per-rule confidence, compliance risks |
| [`docs/OPEN_QUESTIONS.md`](docs/OPEN_QUESTIONS.md) | Decisions required from the business, and the open compliance register |
| **History** | |
| [`docs/ROADMAP.md`](docs/ROADMAP.md), [`docs/FINAL_QA_REPORT.md`](docs/FINAL_QA_REPORT.md), `docs/MILESTONE_*.md` | How the project got here |

## Release stage

The application reports one of four stages in the top bar of every screen, computed from the
database rather than asserted (ADR-037):

| | |
|---|---|
| DEVELOPMENT | Not configured for a business |
| **COMPLIANCE-UNVERIFIED** | **Where this release ships.** Payroll calculates; live payroll is refused |
| READY FOR CONTROLLED TESTING | Every rule verified. Run parallel payrolls and reconcile |
| LIVE PAYROLL ENABLED | A recorded human decision, refused while any rule is unverified |

The engine runs in LIVE mode only on rules graded 🟢 VERIFIED. **No rule currently holds that
grade**, because every Zimbabwean official domain is blocked by this environment's network egress
policy and no primary source could be read — rechecked four times across the project, most
recently for this release. Clearing the gate is a documented, finite task: see
`COMPLIANCE_SPEC.md` §26 — ten documents, roughly one working day.

## Important compliance notice

This software assists with payroll administration. It is **not** a substitute for professional
tax or legal advice. All statutory rates, thresholds, ceilings and methodologies shipped as
seed data must be verified against the current ZIMRA public notices, NSSA gazettes, the Finance
Act and the applicable Statutory Instruments before the system is used to produce a live
payroll. See `docs/COMPLIANCE_ZIMBABWE.md` for the verification status of every rule.

## Collaboration

This project is developed collaboratively, including by more than one AI assistant. Contributors
must read `docs/ARCHITECTURE.md` and `docs/DECISIONS.md` before changing anything, must not
rename established tables, modules or public types without recording an ADR, and must update the
feature-status tables in `docs/ARCHITECTURE.md` as part of the same change.
