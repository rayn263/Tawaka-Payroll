# Tawaka Payroll — Development Roadmap

**Document status:** Proposal, awaiting approval
**Last updated:** 2026-09-12

Each milestone ends with something you can actually run and inspect. Nothing is "done" until it
is testable by you, not merely compiled.

---

## Phase 0 — Foundation (before feature work)

| M | Deliverable | How you test it |
|---|---|---|
| 0.1 | Solution skeleton: the six projects, layering enforced, CI build + test | Build succeeds; a deliberate layering violation fails the build |
| 0.2 | `Money`, `CurrencyCode`, `DateRange` value objects with full unit tests | Run the test suite; try to add USD to ZiG and watch it throw |
| 0.3 | EF Core context, first migration, SQLite file, seeded reference data | Open the `.db` file and inspect the tables |
| 0.4 | Audit interceptor + period-lock interceptor + tests | Change a value, see the audit row; try to write to a locked period, watch it be refused |
| 0.5 | Login, users, roles, permissions | Log in as each role; confirm the sidebar trims |

## Phase 1 — Core payroll (the usable product)

| M | Deliverable | How you test it |
|---|---|---|
| 1.1 | Company profile, settings, logo, currencies, exchange rates | Enter your company details and both currencies; add a rate and see it dated |
| 1.2 | Departments, job titles, locations, projects | Create the real structure of the business |
| 1.3 | Employee management: list, search, filter, sort, add, edit, deactivate; profile tabs | Capture 5–10 real employees across different employment types |
| 1.4 | Employment types and contract versioning; salary change history | Change someone's salary and confirm the old contract is retained, not overwritten |
| 1.5 | Earning types and deduction types with their configurable flags | Create a new allowance in Settings and mark it non-taxable; confirm no code change was needed |
| 1.6 | **Statutory rule configuration screens** — tax tables, credits, exemptions, NSSA, APWCS, levies, all versioned with effective dates and verification status | Enter the verified 2026 tables; add a future-dated 2027 table and confirm both coexist |
| 1.7 | Payroll periods and calendars (monthly, weekly, fortnightly, custom) | Create September 2026 and a weekly period; confirm they don't collide |
| 1.8 | **Calculation engine — single currency**: gross, NSSA, taxable, PAYE, credits, AIDS Levy, deductions, employer costs, net, with full trace | Run the golden-case tests; hand-check one employee against a manual calculation |
| 1.9 | **Calculation engine — dual currency** with the configurable strategy, rate freezing and per-currency tax split | Run one USD employee and one ZiG employee in the same period; confirm no cross-currency addition anywhere |
| 1.10 | Payroll run workflow: draft → calculate → review → approve → finalise (steps 1–8) | Walk a full run; try to approve with a blocking error and confirm you cannot |
| 1.11 | **Calculation preview grid** with drill-down to the trace and previous-period variance | Inspect a full payroll before approving it; click a PAYE figure and read how it was derived |
| 1.12 | Payslip template: screen, print, PDF; zero-value statutory lines; YTD; currency labelling | Generate payslips; print one; compare on-screen and PDF |
| 1.13 | Statutory obligations created on finalisation (CALCULATED + DEDUCTED) | Confirm PAYE, AIDS Levy and NSSA appear in the obligations register, unpaid |
| 1.14 | Core reports: payroll summary, payroll register, net pay, employee earnings/deductions, employer cost — per currency, exportable to PDF/Excel/CSV | Export a register and reconcile it to the payslips |
| 1.15 | Dashboard with counters and warning cards | Confirm each warning fires from a real condition |
| 1.16 | Backup and restore | Back up, change data, restore, verify |

**End of Phase 1: the business can run a real, compliant, auditable monthly and weekly payroll in
both currencies, and knows exactly what it owes.**

## Phase 2 — Operational depth

| M | Deliverable | How you test it |
|---|---|---|
| 2.1 | Statutory approval screen and payment recording (calculated/deducted/approved/paid, reference, date, method, receipt attachment, partial payments) | Approve PAYE, record a payment, confirm the obligation moves to Paid and the dashboard warning clears |
| 2.2 | Outstanding obligations report and due-date reminders | Let a due date pass; confirm the overdue warning |
| 2.3 | Timesheets: capture, approve, feed into payroll | Enter 18 days for a project-based employee and confirm the gross matches days × rate |
| 2.4 | Projects and sites: assignment, labour cost by project | Produce a labour cost report for one project |
| 2.5 | Loans and advances with schedules, balances and automatic payroll deductions | Issue a loan over 6 instalments; run payroll twice; confirm the balance reduces correctly |
| 2.6 | Leave management: types, entitlements, balances, requests, effect on pay | Book leave and confirm the balance and payslip both reflect it |
| 2.7 | Full audit log UI: filter by user, entity, date; view old/new values | Change a salary and find the entry |
| 2.8 | Payroll lock and authorised reopen with reason and diff report | Lock, reopen, change one figure, and read the diff report |
| 2.9 | Casual six-week warning; contract expiry warnings | Create a casual worker near the threshold and confirm the warning |

## Phase 3 — Integration and scale

| M | Deliverable |
|---|---|
| 3.1 | GL account mapping and journal export (CSV/Excel, per currency, balanced) |
| 3.2 | Statutory return exports (P2, P4, ITF16 — per currency) in the required formats |
| 3.3 | Bank payment files, separated by currency and bank |
| 3.4 | Advanced reporting: monthly comparison, labour cost analytics, custom report builder |
| 3.5 | Multi-company |
| 3.6 | Cloud/off-site encrypted backup |
| 3.7 | Employee self-service payslip distribution (email/portal) |
| 3.8 | Optional migration to SQL Server/PostgreSQL for multi-user deployment |

---

## Working method

1. One milestone per pull request, small enough to review.
2. Documentation updated in the same commit as the code — `ARCHITECTURE.md` feature status,
   `DECISIONS.md` for anything structural.
3. The engine gets golden-case tests **before** the UI is built on top of it.
4. No milestone that touches statutory calculation ships without a hand-checked worked example
   recorded in the test suite.
5. Nothing is renamed once established without an ADR — there is a second AI assistant working on
   this codebase, and silent renames are how two assistants destroy each other's work.
