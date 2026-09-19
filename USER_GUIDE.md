# Tawaka Payroll — user guide

Setting a company up and running its payroll. For installing it see `DEPLOYMENT.md`; for backups,
locking and the audit trail see `OPERATIONS.md`.

**The workflow this guide follows, end to end:**

```
 1 Install                    13 Approve the inputs
 2 Create the company         14 Create the payroll period
 3 Configure currencies       15 Calculate
 4 Create users               16 Review, and Explain any figure
 5 Give them roles            17 Approve — a different person
 6 Add employees              18 Finalise
 7 Add contracts              19 Pay the employees
 8 Add earnings/deductions    20 Process the statutory obligations
 9 Add projects and sites     21 Issue payslips
10 Capture attendance         22 Export reports and the GL journal
11 Capture leave              23 Lock the payroll
12 Capture loans              24 Back up
```

---

## 1. Setting the company up

In order, in **Settings**:

1. **Company profile** — legal name, trading name, registration number, BP/TIN, NSSA employer
   number, address. Until this is done the release stage stays DEVELOPMENT.
2. **Currencies** — enable USD, ZiG or both, and set the default payroll currency. Every form in
   the application takes its default from this.
3. **Bank accounts** — one per currency you pay from.
4. **Accounting** — general ledger accounts, per amount type **and per currency**. Only needed if
   you want the accounting journal.

Then **Administration → Users**.

### Users and why you need more than one

**An installation with only the administrator cannot run a payroll.** Approving a payroll is
refused to whoever calculated it, so at least two accounts are needed — in practice an officer and
a manager.

**Administration → Users → Add a user**: username, full name, and one or more roles. The account is
created with a generated password shown once, which the user must change at first sign-in. You do
not choose it and you do not keep it.

| Role | Can | Cannot |
|---|---|---|
| Administrator | Configure, verify rules, manage users, disburse loans, lock payroll | Prepare or approve payroll |
| Payroll Officer | Capture employees, time, leave, loans; calculate; finalise; record payments | **Approve** anything they captured |
| Manager | Approve payroll, time, leave and loans | Prepare payroll or capture inputs |
| Viewer | Read and run reports | Change anything |

Create a user for each person; do not share accounts. The audit trail attributes every action to
whoever was signed in, and approvals turn on who somebody is.

Roles can be changed later (**Roles** on the user's row), a password reset, an account disabled and
a lockout cleared. A user is never deleted — their name is on the payrolls they calculated.

One person cannot hold two roles that between them combine duties no single role is allowed to
hold, such as calculating and approving.

---

## 2. Verifying the statutory rules

**This is what stands between the installation and live payroll.** Until every required rule is
verified the application will calculate a payroll so you can inspect the workings, and will not let
you approve one.

For each rule in **Statutory → Statutory rules**, press **Verify** and record:

- the **document** — "ZIMRA PAYE tables 2026", not a search result that mentioned it;
- **where in it** — the table, the page, the section;
- **the date of the document**.

Verifying without naming a document is refused. A rule marked verified by somebody who cannot say
what they read is worth less than one left unverified, because it looks settled.

If a document turns out to say something else, **Withdraw** the verification with a reason.
Payrolls already run on that rule do not change — they were calculated on what was believed at the
time — but no new live payroll will use it until it is verified again.

**Statutory → Compliance status** shows which open questions this installation is tripping over.
`docs/COMPLIANCE_STATUS.md` states each one: what is unknown, whether it blocks live payroll, and
what document would answer it.

---

## 3. Employees

**Employees → Add employee** captures the person and their first contract on one screen. Nothing is
saved until both halves are valid, so a rejected contract never leaves an employee without terms.

- **Employee number** and **national ID** are unique within the company.
- The **employment type** drives the defaults and decides whether a contract end date is required.
- The **payroll currency** is what this person is paid in. It is never converted.
- Give the **rate** the earnings basis needs — monthly, daily or hourly.

### Afterwards, on the employee's profile

| Tab | What you can do |
|---|---|
| **Personal** | Correct their details |
| **Employment** | End employment — which closes the contract and records the change. There is no delete |
| **Contract** | Change the terms: a new version from a date, with a reason. The old version stays exactly as payroll used it |
| **Earnings** | Standing allowances, in the employee's own pay currency |
| **Deductions** | Standing deductions — medical aid, pension, union |
| **Statutory** | The tax number and NSSA number a payslip and a statutory return need |
| **Payment** | Where the money goes. The account's currency does not have to match the pay |
| Leave, Loans, Payroll history | What has happened to them |

**A pay rise is a new contract version, never an edit.** Payroll for a past period still uses the
version that applied then.

---

## 4. Projects, time, leave and loans

- **Projects & Sites** — projects and the sites under them, so labour cost can be attributed to the
  work it was done on.
- **Time & Leave → Timesheets** — days and hours per employee per period, with overtime by
  category. Capture, then submit.
- **Time & Leave → Leave** — leave requests against a leave type. Capture, then submit.
- **Time & Leave → Calendar** — public holidays, each recorded with its source. None is seeded:
  which days are public holidays is a claim about Zimbabwean law that no seeder is in a position to
  make.
- **Loans & Advances** — a loan or advance, then submit, then approve, then disburse. The recovery
  schedule follows.

**Everything captured must be approved before payroll will use it** — by somebody other than
whoever captured it. **Time & Leave → Approvals** is the queue.

A loan starts in the currency the employee is paid in. Choosing another is allowed and says what it
costs: payroll cannot recover an instalment in a currency it is not paying, and will report it as an
input it did not use.

---

## 5. Running the payroll

```
Create the period → create a run → calculate → review → approve
   → finalise → net wages paid → record statutory payments → lock
```

| Step | Who | What it means |
|---|---|---|
| Create period and run | Officer | **Payroll** → create the period, then **New run** |
| Calculate | Officer | The input snapshot is sealed and hashed at this moment |
| Review | Officer | The preview shows every figure. **Explain** shows how each one was derived, rule by rule |
| Approve | Manager | Refused while any figure is unresolved, and refused to whoever calculated it |
| Finalise | Officer | The money is treated as withheld. **This creates the statutory obligations** |
| Net wages paid | Officer | The employees were paid. Says nothing about the authorities |
| Record statutory payments | Officer | Per obligation, with a reference. **The only thing that marks one paid** |
| Lock | Administrator | Freezes the figures and the approved inputs behind them |

### When a figure shows as a dash

That figure could not be calculated, and the reason is beside it with the compliance question it
depends on. **It is not a zero and must not be treated as one.** A legitimate zero shows as `0.00`.

### When approval is refused

The message says which of these it is: the run is a development calculation; a figure is
unresolved; or you are the person who calculated it.

### When deductions exceed the pay

A recovery larger than what the employee earned leaves no net pay, names the recovery that caused
it, and blocks approval. Reduce or defer the instalment and recalculate. Statutory deductions
cannot cause this on their own.

---

## 6. Payslips

**Payroll → Preview → Payslip**, or from the employee's payroll history. The payslip is rendered
from the stored result and calculates nothing: every figure on it is one the engine produced.

- A payslip from a development run carries a **DEVELOPMENT — NOT FOR STATUTORY USE** watermark.
- Re-issuing produces a new revision; the superseded one stays on the record.
- Printing is the browser print dialogue, which produces A4 and a PDF.

---

## 7. Statutory obligations

**Statutory → Obligations.** Finalising creates one obligation per authority per currency, each
with four states. The fourth is derived, never set:

| State | Set by |
|---|---|
| Calculated | Finalising the run |
| Deducted | Finalising the run (employer-borne obligations are marked not applicable) |
| Approved | A person, explicitly |
| **Paid** | **Unreversed payments alone** |

Approving a payroll marks nothing paid. Marking net wages paid marks nothing paid. Partial payments
are supported and the outstanding balance is shown after each; a payment beyond the balance is
refused.

**Payments** on an obligation's row lists what has been paid against it, and a payment recorded in
error can be **reversed** with a reason: it stays on the record and the amount goes back onto the
balance.

---

## 8. Reports and the GL journal

**Reports**, with a run selector, filters and fourteen report tabs. Every figure reconciles to the
stored payroll result; nothing here calculates payroll.

**USD and ZiG are never added together**, anywhere.

- **Export CSV** writes to `Documents\Tawaka Payroll\Exports`, one table per currency.
- **Payroll inputs** shows what each employee's figures were based on, with the hash of the sealed
  snapshot — and beside it, **inputs this run did not use**, with the reason for each.
- **Accounting journal** needs GL accounts mapped per amount type and per currency. Debits equal
  credits within each currency, and an unmapped amount is reported rather than posted to a default.

---

## 9. Corrections

A locked payroll is never edited. A correction is a **correction run**: its own run, snapshot,
result and obligations, independently auditable. The original stays exactly as it was paid, and the
reports distinguish the two.

---

## 10. What the software will refuse to do

- Run a live payroll on unverified statutory rules.
- Treat an unresolved figure as zero.
- Add USD to ZiG, anywhere, including in a journal.
- Mark a statutory obligation paid because payroll was approved.
- Let one person both capture and approve the same input.
- Edit a locked payroll.
- Delete an employee or a user who has history.
- Store a password in recoverable form, or ship a default one.
- Produce a bank payment file, or a ZIMRA or NSSA submission file — those formats could not be
  obtained from an authoritative source, and inventing one would put a guess in a file sent to a
  bank or an authority.
