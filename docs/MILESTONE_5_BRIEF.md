# Milestone 5 — Payroll inputs

**Time & attendance, leave, holiday calendar, loans and advances**
**Status:** Complete, awaiting approval · **Last updated:** 2026-09-15

---

## 1. The governing constraint

> Everything in Milestone 5 is a payroll **input**. Nothing in it calculates payroll.

The architecture that had to survive this milestone, unchanged:

```
Employee / Contract → Approved Inputs → PayrollInputSnapshot → Pure Engine
                                                                   ↓
                     Payslip / Statutory / Reports ← Persisted PayrollResult
```

Timesheets, leave and loans record **quantities and approvals**: hours, days, categories,
instalments, and who approved what and when. Every conversion of a quantity into money happens in
`TimeAndAbsenceCalculator`, inside the engine, traced like every other figure (ADR-031). There is
no second payroll calculation anywhere in the time, leave, loan or UI code.

One piece of arithmetic stays outside the engine and is named here so it is not mistaken for a
slip: capping a loan deduction at the outstanding balance happens in `LoanService`, because it is a
fact about the loan ledger rather than about pay. The snapshot records the balance it was capped
against, so the deduction stays explicable years later.

---

## 2. The input lifecycle

Every payroll-affecting input shares one lifecycle, in `InputApprovalStatus`:

```
Draft ──submit──▶ Submitted ──approve──▶ Approved ──consumed by a run──▶ Locked
  ▲                   │                     │
  │                   ├──reject──▶ Rejected │
  └──────return───────┴─────────────────────┘
```

| State | Payroll may read it | Editable |
|---|---|---|
| Draft | no | yes |
| Submitted | no | no |
| Approved | **yes** | no |
| Rejected | no | no — kept with its reason |
| Returned | no | yes |
| Locked | **yes** | no — refused at the data layer |

Three rules hold across all three input types:

1. **Payroll consumes Approved and Locked only.** Anything else that exists for the employee and
   period is recorded on the snapshot as a `SkippedInput` with its reason, so the preview can say
   "this employee has an unapproved timesheet" rather than leaving them silently unpaid.
2. **The submitter cannot approve.** Enforced in the service, not assumed of the user.
3. **A correction supersedes; it never edits.** The original stays exactly as the run that consumed
   it saw it.

---

## 3. What each module records

| Module | Records | Does not record |
|---|---|---|
| **Timesheets** | Hours and days per date, project and site, overtime hours by category, public-holiday flags with the calendar entry that set them | What any of it is worth |
| **Leave** | Types, entitlements, an append-only balance ledger, requests with overlap validation, paid/unpaid frozen at request time | What an unpaid day costs |
| **Holiday calendar** | Dates, names, kind, source and verification status | Any shipped holiday |
| **Loans** | Principal, interest, schedule, disbursement, an append-only ledger, over-recovery approval | A stored balance |

---

## 4. What reaches the engine

`PayrollInputSnapshot` gained:

- `TimesheetInput` — now carrying `TimesheetId`, the contributing `TimeEntryIds`, per-project
  `Allocations`, and `Overtime` as a list of `OvertimeInput`, each with its own dated rule and
  **nullable** multiplier.
- `LeaveEffectInput` — days and a paid flag, apportioned to the part of the request inside this
  period.
- `LoanDeductionInput` — the amount, the instalment, the outstanding balance it was capped
  against, and whether over-recovery was approved.
- `HolidayCalendarInput` — the calendar identity and the holiday dates in this period.
- `CasualEngagementInput` — the rolling four-month tally, for the s.12(3) warning.
- `ApprovedInputReference[]` — exactly which approved records this calculation read.
- `SkippedInput[]` — what existed and was not read, and why.
- `Rules.Overtime` and `Rules.PayDivisor` — the new dated rule types.

---

## 5. Compliance position

Three new questions, all recorded rather than guessed:

| # | Question | What the system does meanwhile |
|---|---|---|
| **Q31** | Overtime multipliers: contractual, or mandated by the Labour Act or an NEC CBA? | Prices overtime from dated `OvertimeRules`, seeded Unverified. A category with no multiplier refuses rather than paying plain time |
| **Q32** | Statutory leave entitlements — days, qualification, pay treatment | Every seeded leave type has a **null** entitlement. Balances report as undetermined; days taken are still exact |
| **Q33** | Working days and ordinary hours per month, for converting a salary to a daily or hourly rate | Seeded `PayDivisorRule` at 22 days / 176 hours, Unverified. Overtime for salaried staff and unpaid-leave deductions calculate in development mode only |

On **Q29** the instruction was to keep two claims apart, and they are:
*(a)* this environment cannot retrieve an authoritative ZWG monthly PAYE table — **true and
evidenced**; *(b)* no such table exists — **not claimed**, and nothing in the codebase encodes it.

---

## 6. Acceptance criteria

Each is covered by a test.

### Inputs and approval
- [x] Every payroll-affecting input moves Draft → Submitted → Approved → Rejected/Returned → Locked.
- [x] Payroll consumes approved input only; anything else is recorded as skipped with a reason.
- [x] The user who submitted an input cannot approve it, for all three input types.
- [x] Returning or rejecting an input requires a reason.
- [x] A locked input is immutable at the data layer.
- [x] A correction supersedes its original without altering it.

### Time and attendance
- [x] A day outside the payroll period is refused; the first and last day are accepted.
- [x] A duplicate date, more than 24 hours on one day and a second live timesheet are refused.
- [x] Approved hours drive a time-rated employee's basic pay; no approved time leaves it unresolved.
- [x] Each overtime category is priced by its own dated rule and categories are never merged.
- [x] A category with no established rate refuses rather than paying plain time.
- [x] Project allocations follow hours actually booked and always sum to exactly 100%.

### Leave
- [x] An unestablished entitlement leaves the balance undetermined, never zero.
- [x] Overlapping leave is refused; abutting leave is not.
- [x] Approving posts to the ledger; withdrawing reverses it and keeps both rows.
- [x] Unpaid leave reduces pay at the daily rate; paid leave produces no line at all.
- [x] Leave spanning a period boundary is apportioned, not counted twice.
- [x] Reclassifying a leave type does not reprice leave already taken.

### Holiday calendar
- [x] No public holiday is shipped with the system.
- [x] A captured public holiday starts Unverified; a company holiday needs no citation.
- [x] Removing a holiday deactivates it rather than deleting it.
- [x] The run records the calendar and the holiday dates it used.

### Loans and advances
- [x] Instalments sum exactly to what is repayable.
- [x] The outstanding balance reconciles to the ledger after every movement.
- [x] A deduction is capped at the outstanding balance unless over-recovery is approved by a named
      person with a reason.
- [x] Early settlement cancels the remaining schedule.
- [x] A reversal restores the balance, keeps both rows and cannot be applied twice.
- [x] Calculating a run does not move a loan balance; finalising does.

### Snapshot
- [x] The snapshot is stored, hashed and sealed at calculation.
- [x] It round-trips with its figures and currency intact.
- [x] A tampered snapshot is refused rather than used.
- [x] Approved inputs are queryable in both directions.
- [x] A later repayment or timesheet correction does not change what a completed run recorded.

### Compliance
- [x] The live payroll gate remains closed; no rule was upgraded to Verified.
- [x] Q1, Q6, Q22, Q26, Q29 and Q30 remain explicitly tracked.
- [x] Q29 distinguishes inability to retrieve from evidence of non-existence.
