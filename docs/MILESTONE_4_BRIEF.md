# Milestone 4 — Refined Brief

**Payslips, statutory obligations and reports**
**Status:** In progress · **Last updated:** 2026-09-15

This document refines the Milestone 4 instruction into a buildable specification: the statutory
obligation state machine, and the acceptance criteria the milestone is judged against.

---

## 1. Scope boundary

Milestone 4 builds **on top of** the persisted `PayrollRun` / `PayrollRunEmployee` results from
Milestone 3. The governing constraint:

> **Nothing in Milestone 4 calculates payroll.** The payslip, the obligation register and every
> report read persisted results. If a figure is not already stored against a payroll run, it does
> not appear. Re-deriving a number for display would create a second source of truth, and the two
> would eventually disagree.

Explicitly **not** built (instruction 14): bank payment files and statutory submission files. Both
require format specifications that cannot be obtained (§6), and inventing a layout would embed an
unverified assumption in a file sent to an authority. The **data** for them is modelled and
queryable, so they become a formatting exercise later, not a remodelling one.

---

## 2. The statutory obligation state machine

### 2.1 Why four independent states, not one status

An obligation is not a single progression. A real business routinely sits in a position such as:

> PAYE for September: calculated 225.88, deducted from the employee, approved for payment by the
> director, **not yet paid to ZIMRA**.

A single `Status` field forces that into one word and loses the rest. So the four states are
**independent flags**, each with its own actor, timestamp and evidence, and the display status is
*derived* from them.

### 2.2 The four states

| State | Meaning | Set by | Evidence required |
|---|---|---|---|
| **Calculated** | The engine determined an amount is due | Payroll calculation | The payroll run, and the per-employee lines that sum to it |
| **Deducted** | The amount was actually withheld from employees' pay | Payroll **finalisation** | The finalised run. Not applicable to employer-borne obligations (§2.4) |
| **Approved** | The employer authorised payment to the authority | An explicit, permissioned act (`Statutory.Approve`) | Actor and timestamp |
| **Paid** | The authority actually received the money | **Only** a recorded payment | Date, method, reference, amount, currency, and optionally a receipt |

### 2.3 Derived status

```
Calculated ─▶ Deducted ─▶ Approved ─▶ PartiallyPaid ─▶ Paid
     │            │            │            │
     └────────────┴────────────┴────────────┴──▶ Overdue  (due date passed, not fully paid)
                                              └──▶ Reversed (payment reversed with a reason)
```

`Paid` is **derived**, never stored as a settable flag: an obligation is paid when the sum of its
unreversed payments covers the amount due. There is no code path that sets paid without a payment
row.

### 2.4 Employer-borne obligations

NSSA employer, APWCS, ZIMDEF, SDF and the employer share of NEC are **employer costs**, not
employee deductions. For these, `Deducted` is **not applicable** rather than false — the register
shows `n/a`, and approval does not require a deduction. Recording them as "not deducted" would
read as an outstanding deduction that never happens.

### 2.5 Transition rules (invariants)

1. **Calculated** is produced by the payroll run. It cannot be entered by hand.
2. **Deducted** is set only by finalising the run that calculated it. Finalising a run sets
   `Deducted` on every employee-borne obligation it produced, and nothing else.
3. **Approved** requires `Calculated`, and requires `Deducted` where deduction is applicable.
   Approving an obligation whose money was never withheld would authorise paying over money the
   business does not hold.
4. **Payment** requires `Approved`. A payment carries date, method, reference, amount and currency;
   a payment without a reference is refused.
5. **A payment may not exceed the outstanding balance** unless it is explicitly flagged as
   including penalty or interest, which is recorded separately from the obligation itself.
6. **Currency is fixed.** A payment must be in the obligation's currency. A USD obligation cannot
   be settled with a ZiG payment; that would be a conversion, which is a separate, recorded act.
7. **Reversal** of a payment requires a reason, never deletes the payment row, and returns the
   obligation to its previous derived status.
8. **Approving a payroll run never marks any obligation paid.** This is the rule the whole design
   exists to enforce.
9. An obligation belonging to a **locked** payroll run cannot be altered, except by recording a
   payment — which is a later, separate event and is always permitted while the obligation is
   outstanding.
10. Every transition writes to the append-only audit trail with actor, timestamp and old/new value.

### 2.6 What the register must always be able to answer

- What do we owe, to whom, for which period, in which currency?
- Of that, what has been deducted from employees and is therefore money we are holding?
- What has been approved for payment but not yet paid?
- What is overdue, and by how long?
- For any payment: when, how much, by what method, under what reference, authorised by whom.

---

## 3. Payslip requirements

1. **Canonical model.** One `PayslipDocument` built from persisted results, rendered by one
   template. Screen, print and PDF share it, so they cannot drift.
2. **Renders, never calculates.** The document assembles stored lines and totals. The only
   arithmetic permitted is presentational grouping that must reconcile exactly to stored totals,
   and a test asserts that reconciliation.
3. **Three distinct value states**, visually distinguishable:
   - **Calculated** — a real figure, shown normally.
   - **Zero** — a legitimate calculated `0.00`, shown as `0.00`. Configured statutory categories
     appear even at zero.
   - **Unresolved** — the rule was missing or unverified, shown as `—` with a footnote. Never
     `0.00`.
4. **Currency.** Every amount carries its currency. A payslip is denominated in one currency;
   where a line originated in another, the original amount, rate, rate date and source are shown
   alongside the converted figure.
5. **Statutory status block.** Shows, per statutory item, whether it was deducted, approved and
   remitted — without asserting payment that has not been recorded.
6. **Employer contributions** are shown separately and labelled as not deducted from the employee.
7. **Development watermark.** A payslip from a development-mode run is watermarked
   `DEVELOPMENT — NOT FOR STATUTORY USE` and cannot be issued.

---

## 4. Reports

Every report is **per currency**. There is no consolidated total that adds USD to ZiG; a
consolidated view, when built, must state the rate, its date and both original totals.

| Report | Contents |
|---|---|
| Payroll summary | Per currency: employees, gross, taxable, PAYE, AIDS Levy, NSSA employee, other deductions, net pay, employer cost |
| Payroll register | Per employee, every line — the detail behind the summary |
| Deductions report | Per deduction type, per currency, with employee counts |
| Statutory report | Per obligation type and currency: calculated, deducted, approved, paid, outstanding |
| Employer cost report | Per employer cost type: NSSA employer, APWCS, ZIMDEF, SDF, plus gross, giving total cost of employment |
| Project and site labour cost | Allocated cost by project, then site, per currency |
| Currency summary | Side-by-side totals per currency, explicitly never summed |

---

## 5. Acceptance criteria

Milestone 4 is complete when **all** of the following hold. Each is covered by a test.

### Payslip
- [ ] A payslip renders from persisted results only; no payroll arithmetic exists in the payslip
      code path.
- [ ] Payslip totals reconcile exactly to the stored run-employee totals.
- [ ] A legitimate zero renders `0.00`; an unresolved figure renders `—`; the two are never
      confused.
- [ ] Configured statutory categories appear even when zero.
- [ ] A USD payslip and a ZiG payslip each show their own currency throughout.
- [ ] A development-mode payslip is watermarked and cannot be issued.
- [ ] A payslip is numbered, and re-issuing after a correction produces a new revision without
      destroying the previous one.

### Statutory obligations
- [ ] Finalising a run creates obligations with `Calculated` and `Deducted` set, and `Approved`
      and `Paid` **not** set.
- [ ] Approving a payroll run does **not** mark any obligation paid.
- [ ] An obligation cannot be approved before it is calculated and (where applicable) deducted.
- [ ] A payment cannot be recorded before approval.
- [ ] A payment without a reference is refused.
- [ ] A payment in the wrong currency is refused.
- [ ] Partial payment leaves the obligation `PartiallyPaid` with the correct outstanding balance.
- [ ] `Paid` is derived from payments; no code path sets it directly.
- [ ] Reversing a payment requires a reason, retains the payment row and restores the balance.
- [ ] Employer-borne obligations show `Deducted` as not applicable.
- [ ] Every obligation reconciles to the sum of its per-employee lines.
- [ ] Every transition is audited with actor and timestamp.

### Workflow
- [ ] Review → Approve → Finalise → Paid → Locked, each permissioned.
- [ ] The user who calculated a run cannot approve it.
- [ ] A run cannot be finalised before approval.
- [ ] A locked run is immutable at the data layer.
- [ ] Reopening a locked run requires the `Payroll.Reopen` permission and a reason.
- [ ] A correction run is isolated: it does not alter the original run's figures or obligations.

### Reports
- [ ] Every report groups by currency and never sums across currencies.
- [ ] The payroll summary reconciles to the payroll register.
- [ ] The employer cost report reconciles: gross + employer contributions = total employer cost.
- [ ] The statutory report reconciles to the obligation register.
- [ ] Project labour cost allocations sum to the total employer cost.

### Reproducibility and compliance
- [ ] Recalculating a past period after a salary change reproduces the original figures.
- [ ] Recalculating after a rule change reproduces the original figures.
- [ ] The rule snapshot used by a run is retained and readable.
- [ ] The live payroll gate remains closed; no rule is upgraded to Verified without authoritative
      evidence.
- [ ] Q1, Q6, Q22 and Q26 remain explicitly tracked.

---

## 6. Data available for future statutory submissions

Instruction 9 requires that a future ZIMRA or NSSA submission needs no remodelling. The following
is already queryable per period and currency, without new tables:

| Requirement | Source |
|---|---|
| Per-employee gross, taxable, PAYE, AIDS Levy | `PayrollRunEmployees` |
| Per-employee NSSA employee and employer | `PayrollRunEmployees`, `PayrollEmployerCostLines` |
| Employee tax number and NSSA number | `EmployeeStatutoryProfiles` |
| Employer tax, NSSA, ZIMDEF, SDF and NEC identifiers | `Companies` |
| Separate totals per currency | Currency held on every payroll row |
| Amount due, deducted, approved, paid per obligation | `StatutoryObligations`, `StatutoryPayments` |
| Per-employee breakdown behind each obligation | `StatutoryObligationLines` |
| Which rule version produced each figure | `PayrollRuns.RuleSnapshot`, `PayrollCalculationTraces` |

What is **missing** is only the *format* of each return — the field layout of P2/REV5, ITF16, P4
and WC50. Those remain compliance question **Q25** and are not guessed.
