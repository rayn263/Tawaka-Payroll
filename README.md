# Tawaka Payroll

A professional, Windows-based payroll management system for businesses operating in Zimbabwe.

> **Status: ARCHITECTURE PROPOSAL — NOT YET APPROVED. NO APPLICATION CODE HAS BEEN WRITTEN.**
> This repository currently contains design documentation only. Development begins after the
> architecture in `docs/ARCHITECTURE.md` is approved and the open questions in
> `docs/OPEN_QUESTIONS.md` are answered.

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

4. **Payroll data does not silently change.**
   Every create, update and approval is written to an append-only audit trail with the user,
   timestamp, old value and new value. Locked payroll periods are immutable at the data layer,
   not merely hidden in the user interface.

## Documentation

| Document | Contents |
|---|---|
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | Technology stack, application architecture, navigation, calculation pipeline, payslip design, security model, file structure, feature status |
| [`docs/DATABASE_SCHEMA.md`](docs/DATABASE_SCHEMA.md) | Full relational schema, entity relationships, money and currency representation |
| [`docs/COMPLIANCE_ZIMBABWE.md`](docs/COMPLIANCE_ZIMBABWE.md) | Zimbabwe statutory requirements, source references, verification status, per-rule confidence, compliance risks |
| [`docs/ROADMAP.md`](docs/ROADMAP.md) | Phased development plan with testable milestones |
| [`docs/DECISIONS.md`](docs/DECISIONS.md) | Architecture decision record (ADR) log |
| [`docs/OPEN_QUESTIONS.md`](docs/OPEN_QUESTIONS.md) | Decisions required from the business before or during development |

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
