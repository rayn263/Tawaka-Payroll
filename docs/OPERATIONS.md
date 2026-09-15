# Tawaka Payroll — Operations guide

Installing, running, verifying and backing up the application. For building it on Windows see
`WINDOWS-BUILD.md`; for the architecture see `docs/ARCHITECTURE.md`; for the statutory position see
`ZIMBABWE_PAYROLL_COMPLIANCE_SPEC_V1.md`.

---

## 1. What the release stage means

The top bar of every screen shows one of four stages. It is computed from the database, not set by
anybody (ADR-037):

| Stage | What it means | How you leave it |
|---|---|---|
| **DEVELOPMENT** | No company details, or no employees. Demonstration only | Complete the company profile and add employees |
| **COMPLIANCE-UNVERIFIED** | Configured and usable, but statutory rules have never been checked against an authoritative source. Payroll calculates so the workings can be inspected; it cannot be approved | Verify every rule (§3 below) |
| **READY FOR CONTROLLED TESTING** | Every rule verified. Run payrolls in parallel with your existing process and reconcile | Nothing the software can do. A person decides |
| **LIVE PAYROLL ENABLED** | A recorded decision, with a reason, after parallel running | — |

**This release ships as COMPLIANCE-UNVERIFIED**, and that is the honest position: no statutory
figure in it has been read from ZIMRA, NSSA or a Statutory Instrument.

---

## 2. First run

1. Start the application. It creates `%LOCALAPPDATA%\Tawaka Payroll\`, its database, and applies
   every migration.
2. A dialog shows a **generated administrator password**. Write it down; it is shown once and
   stored only as a hash. There is no default password anywhere in this software.
3. Sign in as `admin` and change the password when prompted.
4. **Settings → Company profile**: legal name, registration number, BP/TIN, NSSA employer number,
   address. Until this is done the release stage stays DEVELOPMENT.
5. **Settings → Currencies**: enable USD, ZiG or both, and set the default payroll currency.
6. **Administration → Users & roles**: create a user per person. Do not share accounts — the audit
   trail attributes every action to whoever was signed in.

### Segregation of duties

The four shipped roles are built so that no one person can both create and approve. The
application enforces it; it does not merely recommend it.

| Role | Can | Cannot |
|---|---|---|
| Administrator | Configure, verify rules, manage users, disburse loans, lock payroll | Prepare or approve payroll |
| Payroll Officer | Capture employees, time, leave, loans; calculate; finalise; record payments | **Approve** anything they captured |
| Manager | Approve payroll, time, leave and loans | Prepare payroll or capture inputs |
| Viewer | Read and run reports | Change anything |

---

## 3. Verifying statutory rules

This is the work that moves the installation to READY FOR CONTROLLED TESTING. It needs somebody
with access to the source documents; the software cannot do it.

For each rule in **Statutory → Statutory rules**:

1. Open the current publication — the ZIMRA PAYE tables, the NSSA contribution notice, the Finance
   Act, the applicable Statutory Instrument or your NEC collective bargaining agreement.
2. Compare every figure: bands, rates, ceilings, credits, thresholds, the fixed-deduction column.
3. Correct anything that differs, recording the source and its date.
4. Mark the rule **Verified**.

`ZIMBABWE_PAYROLL_COMPLIANCE_SPEC_V1.md` §26 lists the documents. §25 lists the questions that
must be answered before the figures can be trusted at all — Q1, Q6, Q22, Q26, Q29, Q30, Q31, Q32
and Q33. **Statutory → Compliance status** shows which of them this installation is tripping over.

Leave entitlements (Q32) and public holidays are verified the same way, on their own screens.

---

## 4. Running a payroll

```
Capture inputs → approve inputs → create the run → calculate → review → approve
   → finalise → net wages paid → record statutory payments → lock
```

| Step | Who | What it means |
|---|---|---|
| Capture timesheets, leave, loans | Officer | Quantities and dates. Nothing here prices anything |
| Approve inputs | Manager | Payroll consumes approved inputs only; anything unapproved is reported, never silently skipped |
| Create and calculate the run | Officer | The snapshot is sealed and hashed at this moment |
| Review | Officer | The preview shows every figure, and "Explain" shows how each was derived |
| Approve | Manager | Refused while any figure is unresolved, and refused to whoever calculated it |
| Finalise | Officer | Money is treated as withheld. **This is what creates the statutory obligations** |
| Net wages paid | Officer | Employees were paid. Says nothing about the authorities |
| Record statutory payments | Officer | Per obligation, with a reference. **The only thing that marks one paid** |
| Lock | Administrator | Freezes the figures and the inputs behind them |

A correction after a lock is a **correction run**, never an edit. The original stays exactly as it
was paid.

---

## 5. Backup and restore

**Settings → Backup & restore.**

A backup is a consistent copy taken through SQLite's own backup mechanism, plus a manifest naming
the schema, the time, the person and the size. Take one before every payroll run and before any
upgrade.

Restoring **replaces all current payroll data**. The application sets the database in use aside as
`tawaka-payroll.db.replaced-<timestamp>` first, so a mistaken restore can be undone by renaming
that file back. A backup taken on a newer version is refused rather than restored.

Close and reopen the application after a restore.

---

## 6. Where files live

| | |
|---|---|
| Database | `%LOCALAPPDATA%\Tawaka Payroll\tawaka-payroll.db` |
| Error logs | `%LOCALAPPDATA%\Tawaka Payroll\logs\` |
| Report exports | `%USERPROFILE%\Documents\Tawaka Payroll\Exports\` |
| Backups | wherever you choose |

---

## 7. Demonstration data

For training or evaluation, seed a worked example — eight employees across both currencies and
every employment type, with approved time, leave and a loan:

```powershell
dotnet run --project tools/Tawaka.Foundation.Cli -- --demo-data
```

It refuses outright if the database already holds employees, stamps the installation as
demonstration data, appends "[DEMONSTRATION]" to the company name, and puts a banner on every
screen. Never seed it into a database you intend to use for real payroll.

---

## 8. Getting figures out

Every report exports to CSV from **Reports → Export CSV**, written to
`Documents\Tawaka Payroll\Exports`. Payslips and reports print to A4 through the browser print
dialogue, which also produces a PDF.

The accounting journal needs general ledger accounts mapped first, per amount **and per currency**,
in **Settings → Accounting**. An amount with no mapped account appears on the journal as unmapped
rather than being posted to a default.

---

## 9. What this software will not do

- It will not run a live payroll on unverified statutory rules.
- It will not treat an unresolved figure as zero.
- It will not add USD to ZiG, anywhere, including in a journal.
- It will not mark a statutory obligation paid because payroll was approved.
- It will not let one person both capture and approve the same input.
- It will not edit a locked payroll.
- It will not generate a bank payment file or a ZIMRA or NSSA submission file, because those
  formats could not be obtained from an authoritative source and inventing one would put a guess in
  a file sent to a bank or an authority.
