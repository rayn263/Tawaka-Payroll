# Tawaka Payroll

A professional, Windows-based payroll management system for businesses operating in Zimbabwe.

> **Status: Milestone 5 (Time, attendance, leave and loans) complete — awaiting approval.**
> Payroll now runs on controlled, approved inputs: timesheets with configurable overtime
> categories, leave with entitlements and balances, a captured holiday calendar, and loans and
> advances recovered through payroll. Each moves through Draft → Submitted → Approved → Locked, and
> payroll consumes nothing that has not been approved. The snapshot each calculation ran on is
> stored, hashed and sealed, so a completed run reproduces against the inputs it actually had.
> **No live payroll can be produced**: every seeded statutory rule is unverified, so the engine
> calculates in development mode only and the gate refuses approval. See `PROJECT_STATE.md`.

## Quick start

```bash
apt-get install -y dotnet-sdk-8.0   # or install the .NET 8 SDK for your platform
./build.sh                          # build the cross-platform solution
./test.sh                           # 608 passing, 1 skipped (pending Q4a/Q22)
./foundation-check.sh               # migrate, seed, print the rule register, run the live gate
dotnet run --project tools/Tawaka.Foundation.Cli -- --payroll                  # gate closed: calculation only
dotnet run --project tools/Tawaka.Foundation.Cli -- --payroll --verify-rules   # simulated verification: inputs, calculation, obligations
```

All screens live in `src/Tawaka.Ui.Shared` and compile anywhere. The Windows host builds from
`Tawaka.Payroll.sln` on Windows only.

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
| [`ZIMBABWE_PAYROLL_COMPLIANCE_SPEC_V1.md`](ZIMBABWE_PAYROLL_COMPLIANCE_SPEC_V1.md) | **Authoritative specification for the payroll calculation engine.** Statutory rules with per-rule confidence grades, dual-currency methodology, 34 test cases, verification checklist, blocking decision register |
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | Technology stack, application architecture, navigation, calculation pipeline, payslip design, security model, file structure, feature status |
| [`docs/DATABASE_SCHEMA.md`](docs/DATABASE_SCHEMA.md) | Full relational schema, entity relationships, money and currency representation |
| [`docs/COMPLIANCE_ZIMBABWE.md`](docs/COMPLIANCE_ZIMBABWE.md) | Zimbabwe statutory requirements, source references, verification status, per-rule confidence, compliance risks |
| [`PROJECT_STATE.md`](PROJECT_STATE.md) | **Where the project actually is**: phase, milestone, architecture as built, database version, rules implemented and verified, known issues, next milestone |
| [`TESTING.md`](TESTING.md) | How to run the tests, the behaviour/seed split, and the 34-case compliance catalogue |
| [`docs/ROADMAP.md`](docs/ROADMAP.md) | Phased development plan with testable milestones |
| [`docs/DECISIONS.md`](docs/DECISIONS.md) | Architecture decision record (ADR) log |
| [`docs/OPEN_QUESTIONS.md`](docs/OPEN_QUESTIONS.md) | Decisions required from the business before or during development |

## Verification gate

The payroll engine runs in LIVE mode only on rules graded 🟢 VERIFIED. **No rule currently holds
that grade**, because every Zimbabwean official domain is blocked by this environment's network
egress policy and no primary source could be read. Clearing the gate is a documented, finite task:
see `ZIMBABWE_PAYROLL_COMPLIANCE_SPEC_V1.md` §26 — ten documents, roughly one working day.

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
