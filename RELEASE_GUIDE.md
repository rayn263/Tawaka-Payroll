# Tawaka Payroll — Release and operations guide

The one authoritative guide: installing it, starting it, setting up a company, running a payroll,
paying the authorities, getting figures out, backing it up, and knowing what it will refuse to do.

Read §1 before anything else.

| Also see | |
|---|---|
| Compliance position | `docs/COMPLIANCE_STATUS.md` |
| Remaining Windows validation | `docs/WINDOWS_VALIDATION.md` |
| Architecture and decisions | `docs/ARCHITECTURE.md`, `docs/DECISIONS.md` |
| Statutory research | `ZIMBABWE_PAYROLL_COMPLIANCE_SPEC_V1.md` |
| Tests | `TESTING.md` |

---

## 1. What this release is, and is not

Tawaka Payroll is a Windows desktop payroll application for a Zimbabwean business, built around one
rule: **it will not produce a figure it cannot account for.**

**It is not approved for live payroll, and it will not let you run one.** No statutory figure in it
has been read from ZIMRA, NSSA or a Statutory Instrument, so every statutory rule is `Unverified`
and the installation sits at **COMPLIANCE-UNVERIFIED**. A payroll will calculate, so you can inspect
the workings, and it cannot be approved, finalised or paid. See `docs/COMPLIANCE_STATUS.md` for
exactly which questions are open and what would answer them.

**The Windows host has not been run.** It is built and tested on Linux, which cannot compile WPF, so
the window has never been seen on a screen. `docs/WINDOWS_VALIDATION.md` is the procedure that
closes that gap, and until somebody works through it this release candidate is not a release.

### The four release stages

Shown in the top bar of every screen. Computed from the database, not set by anybody (ADR-037):

| Stage | What it means | How you leave it |
|---|---|---|
| **DEVELOPMENT** | No company details, or no employees. Demonstration only | Complete the company profile and add employees |
| **COMPLIANCE-UNVERIFIED** | Configured and usable, but statutory rules have never been checked against an authoritative source. Payroll calculates so the workings can be inspected; it cannot be approved | Verify every rule (§6) |
| **READY FOR CONTROLLED TESTING** | Every rule verified. Run payrolls in parallel with your existing process and reconcile | Nothing the software can do. A person decides |
| **LIVE PAYROLL ENABLED** | A recorded decision, with a reason, after parallel running | — |

---

## 2. Windows requirements

| | |
|---|---|
| Windows | 10 version 1809 or later, or Windows 11 (x64) |
| WebView2 Runtime | Pre-installed on Windows 11 and up-to-date Windows 10. Otherwise install Microsoft's **Evergreen Standalone Installer** |
| Disk | 200 MB, plus the database — a few MB per year for a hundred employees |
| Rights | None special. Everything lives in the user's own profile |
| .NET runtime | Not needed for the self-contained build. Needed (8.0) only if you run from source |

---

## 3. Installation

### From the self-contained executable

1. Copy `Tawaka.Payroll.exe` to the machine — anywhere the user can write, e.g.
   `C:\Tawaka Payroll\`.
2. Install the WebView2 Evergreen runtime if the machine does not already have it.
3. Double-click it. There is no installer, no registry entry and nothing to uninstall: deleting the
   folder and `%LOCALAPPDATA%\Tawaka Payroll\` removes it completely.

### Building it yourself

Needs the .NET 8.0 SDK ([dotnet.microsoft.com/download/dotnet/8.0](https://dotnet.microsoft.com/download/dotnet/8.0));
check with `dotnet --list-sdks` that an entry beginning `8.0.` is present.

```powershell
git clone <repository-url> tawaka-payroll
cd tawaka-payroll

# The full solution, including the Windows host.
dotnet build Tawaka.Payroll.sln -c Release

# Run it.
dotnet run --project src/Tawaka.Ui.Desktop -c Release

# Or produce the single self-contained executable.
dotnet publish src/Tawaka.Ui.Desktop -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Output: `src\Tawaka.Ui.Desktop\bin\Release\net8.0-windows\win-x64\publish\Tawaka.Payroll.exe`.

`Tawaka.Payroll.Core.sln` deliberately excludes the Windows host so everything else builds and
tests on Linux and macOS. `Tawaka.Payroll.sln` includes it.

> If `Microsoft.AspNetCore.Components.WebView.Wpf` 8.0.100 fails to restore, check the current 8.0.x
> version on nuget.org and update the `PackageReference` in
> `src/Tawaka.Ui.Desktop/Tawaka.Ui.Desktop.csproj`. That version is pinned and has never been
> restored on Windows from this repository.

---

## 4. First startup and the first administrator

On first run the application creates `%LOCALAPPDATA%\Tawaka Payroll\`, creates its database there,
applies every migration and seeds the statutory baseline, the company shell, the permissions and the
four roles.

It then shows a dialog with a **generated administrator password**.

- **Write it down before clicking OK.** It is shown once, stored only as a PBKDF2-HMAC-SHA256 hash
  with a per-user salt, and cannot be recovered by anybody, including whoever wrote this software.
- There is **no default password** anywhere in Tawaka Payroll. A shipped default is a shipped
  vulnerability.
- Sign in as `admin` and change the password when prompted. You cannot get past that prompt without
  changing it.

If the password is lost before it is changed, there is no recovery: delete
`%LOCALAPPDATA%\Tawaka Payroll\tawaka-payroll.db` and start again. Do not do that to a database with
payroll in it — restore a backup instead.

---

## 5. Company setup

In order:

1. **Settings → Company profile** — legal name, trading name, registration number, BP/TIN, NSSA
   employer number, address. Until this is done the release stage stays DEVELOPMENT.
2. **Settings → Currencies** — enable USD, ZiG or both, and set the default payroll currency. Every
   form on every screen takes its default from this.
3. **Settings → Bank accounts** — one per currency you pay from.
4. **Settings → Accounting** — general ledger accounts, per amount type **and per currency**. Needed
   only if you want the accounting journal.
5. **Administration → Users** — an account for each person, with their role. See below.

### Users and segregation of duties

**Create a user for each person. Do not share accounts:** the audit trail attributes every action to
whoever was signed in, and approvals turn on who someone is.

An installation with only the administrator **cannot run a payroll**. Approval refuses whoever
calculated the run, so at least two accounts are needed — in practice an officer and a manager.

Add each person in **Administration → Users → Add a user**: username, full name, and one or more
roles. The account is created with a generated password shown once, which the user must change at
first sign-in. You do not choose it and you do not keep it.

| Role | Can | Cannot |
|---|---|---|
| Administrator | Configure, verify rules, manage users, disburse loans, lock payroll | Prepare or approve payroll |
| Payroll Officer | Capture employees, time, leave, loans; calculate; finalise; record payments | **Approve** anything they captured |
| Manager | Approve payroll, time, leave and loans | Prepare payroll or capture inputs |
| Viewer | Read and run reports | Change anything |

The conflict rules are enforced, not recommended, and they are enforced across a user's roles taken
together: Payroll Officer and Manager are each permissible, and one person cannot hold both.

A user is never deleted — their name is on the payrolls they calculated — but an account can be
**disabled**, a password **reset**, and a lockout after repeated failed sign-ins **cleared**. The
last account that can manage users cannot be disabled, and nobody can disable their own.

---

## 6. Verifying statutory rules

This is the work that moves the installation to READY FOR CONTROLLED TESTING. It needs somebody with
access to the source documents. **The software cannot do it, and will not pretend to.**

For each rule in **Statutory → Statutory rules**:

1. Open the current publication — the ZIMRA PAYE tables, the NSSA contribution notice, the Finance
   Act, the applicable Statutory Instrument, or your NEC collective bargaining agreement.
2. Compare every figure: bands, rates, ceilings, credits, thresholds, and the fixed-deduction
   ("less") column.
3. Correct anything that differs, recording the source and its date on the rule.
4. Mark the rule **Verified**.

**Statutory → Compliance status** shows which open questions this installation is tripping over, and
`docs/COMPLIANCE_STATUS.md` states each one in full: what is unknown, whether it blocks live
payroll, and what document would answer it.

Leave entitlements and public holidays are verified the same way, on their own screens. The seed
ships **no public holidays**, because which days are public holidays in a given year is a claim
about Zimbabwean law that a seeder is in no position to make.

---

## 7. Employee setup

**Employees → Add employee** captures the person and their first contract on one screen. Nothing is
saved until both halves are valid, so a rejected contract never leaves an employee stranded without
terms.

- **Employee number** and **national ID** are unique within the company.
- The **employment type** drives the defaults and decides whether a contract end date is required.
- The **payroll currency** is the currency this person is paid in. It is not converted anywhere.
- **Rates**: give the one the earnings basis needs — monthly, daily or hourly.

Afterwards, on the employee's profile: statutory identifiers (BP/TIN, NSSA number), recurring
earnings and deductions, payment account, leave entitlements.

Changing terms later creates a **new contract version**. The previous version is never overwritten,
and a past payroll keeps the version it was run on.

---

## 8. Running a payroll

```
Capture inputs → approve inputs → create the run → calculate → review → approve
   → finalise → net wages paid → record statutory payments → lock
```

| Step | Who | What it means |
|---|---|---|
| Capture timesheets, leave, loans | Officer | Quantities and dates. Nothing here prices anything |
| Approve inputs | Manager | Payroll consumes approved inputs only. Anything left out is reported, never silently skipped |
| Create and calculate the run | Officer | The input snapshot is sealed and hashed at this moment |
| Review | Officer | The preview shows every figure, and **Explain** shows how each one was derived |
| Approve | Manager | Refused while any figure is unresolved, and refused to whoever calculated it |
| Finalise | Officer | The money is treated as withheld. **This is what creates the statutory obligations** |
| Net wages paid | Officer | The employees were paid. Says nothing about the authorities |
| Record statutory payments | Officer | Per obligation, with a reference. **The only thing that marks one paid** |
| Lock | Administrator | Freezes the figures and the approved inputs behind them |

### Approvals

Approval is refused when the run is in Development mode, when any figure is unresolved, and when the
person approving is the person who calculated. None of these is a warning that can be clicked
through.

### Payslips

**Payroll → Preview → Payslip**, or from the employee's payroll history. The payslip is rendered
from the stored result and performs no calculation of its own: every figure on it is one the engine
produced and the database holds.

- A payslip from a Development run carries a **DEVELOPMENT — NOT FOR STATUTORY USE** watermark.
- A figure that could not be calculated appears as a dash, never as `0.00`. A legitimate zero
  appears as `0.00`.
- Re-issuing produces a new revision, and the superseded one stays on the record.
- Printing is the browser print dialogue, which produces A4 and a PDF.

### Corrections after a lock

A locked payroll is never edited. A correction is a **correction run**: its own run, its own
snapshot, its own result and its own obligations, independently auditable. The original stays
exactly as it was paid, and the reports distinguish the two.

---

## 9. Statutory obligations

**Statutory → Obligations.** Finalising a payroll creates one obligation per authority per currency.
Each carries four independent states, and the fourth is derived, never set:

| State | Set by | Means |
|---|---|---|
| Calculated | Finalising the run | The amount is known |
| Deducted | Finalising the run | It was withheld from the employees (employer-borne obligations are marked not applicable) |
| Approved | A person, explicitly | It is agreed and payable |
| **Paid** | **Derived from unreversed payments alone** | The authority actually has the money |

An obligation is paid when payments recorded against it, less any reversed, cover it in full.
Approving a payroll does not mark anything paid; marking the employees' net wages as paid does not
either. Partial payments are supported, the outstanding balance is shown after each, and a payment
that would exceed the balance is refused. A reversal requires a reason, keeps the original payment
on the record, and puts the money back on the balance.

Employer-borne obligations — APWCS, the employer's share of NSSA, levies — are never turned into
employee deductions.

---

## 10. Reports

**Reports**, with a run selector, filters (currency, department, project, employment type) and
fourteen report tabs. Every figure reconciles to the persisted payroll result; nothing on this
screen calculates payroll.

**USD and ZiG are never added together**, anywhere: every monetary section of every report is
grouped by currency first, and the report model has no field that could hold a cross-currency total.

- **Export CSV** writes to `Documents\Tawaka Payroll\Exports`, one table per currency.
- **Payroll inputs** lists what each employee's figures were based on, with the hash of the sealed
  snapshot — and, beside it, **inputs this run did not use**, with the reason each was left out.
- **Accounting journal** needs GL accounts mapped per amount type and per currency. Debits equal
  credits within each currency, and a currency's journal is a journal on its own. An amount with no
  mapped account is reported as unmapped rather than posted to a default.

---

## 11. Backups

**Settings → Backup & restore.**

A backup is a consistent copy taken through SQLite's own backup mechanism — not a file copy — plus a
manifest naming the application, the schema version, every applied migration, the time, the person
and the size.

**Take one before every payroll run and before every upgrade.** Keep them off the machine: a backup
on the same disk protects you from a mistake, not from a failure.

## 12. Restoring

Restoring **replaces all current payroll data** and must be confirmed explicitly.

1. The application checks the backup first: a folder that is not a backup, or one whose database no
   longer matches its manifest, is refused rather than attempted.
2. The database in use is set aside as `tawaka-payroll.db.replaced-<timestamp>` — never overwritten.
3. The backup is copied into place. If that fails, the displaced database is put straight back.
4. **Close and reopen the application.**

A mistaken restore is undone by closing the application, deleting the restored
`tawaka-payroll.db`, and renaming the `.replaced-<timestamp>` file back. A backup taken on a newer
version of the software is refused; install that version first.

---

## 13. Locking, and what it protects

Locking a payroll run freezes the run, its result figures, every earning, deduction and
employer-cost line, its statutory obligations, its sealed input snapshot, and the timesheets, leave
and loans it consumed. The protection is in the data layer, not in the screens: an attempt to change
any of it fails wherever it comes from.

Statutory payments are a **later** event and remain possible after the lock — the money still has to
be paid and recorded.

---

## 14. The audit trail

**Administration → Audit.** Append-only and per-field: what changed, from what, to what, when, by
whom, on which machine. Password hashes and salts are excluded — there is nothing there worth
recording and much worth not recording.

Filter by date, user, record type, action or free text. The history of any single record is
available from that record.

---

## 15. Demonstration data

For training or evaluation — eight employees across both currencies and every employment type, with
approved time, leave and a loan:

```powershell
dotnet run --project tools/Tawaka.Foundation.Cli -- --demo-data
```

It refuses outright if the database already holds any employee, stamps the installation as
demonstration data, appends "[DEMONSTRATION]" to the company name, creates its payroll period in
Development mode, and puts a banner on every screen until the stamp is removed.

**Never seed it into a database you intend to use for real payroll.**

---

## 16. Where files live

| | |
|---|---|
| Database | `%LOCALAPPDATA%\Tawaka Payroll\tawaka-payroll.db` |
| Error logs | `%LOCALAPPDATA%\Tawaka Payroll\logs\error-yyyyMMdd.log` |
| Report exports | `%USERPROFILE%\Documents\Tawaka Payroll\Exports\` |
| Backups | wherever you choose in Settings → Backup & restore |

Per-user application data, so the application needs no administrator rights and the database is
included in an ordinary Windows profile backup.

---

## 17. Troubleshooting

**"WebView2 runtime not found"** — install Microsoft's Evergreen Standalone Installer.

**The window opens blank** — the host page is `wwwroot/index.html` in the desktop project and the
stylesheet comes from `_content/Tawaka.Ui.Shared/app.css`. A blank window usually means the Razor
class library's static assets were not copied; a clean rebuild resolves it.

**"Tawaka Payroll could not prepare its database"** — the message names the database path. The usual
cause is a database restored from a backup taken on a newer version; install that version first. The
full exception is in `logs\`.

**Startup fails after a restore** — the displaced database is next to the original as
`tawaka-payroll.db.replaced-<timestamp>`. Close the application, delete the restored file and rename
that one back.

**An account is locked out** — five failed sign-ins lock an account for fifteen minutes. An
administrator can clear it immediately in **Administration → Users → Unlock**.

**A figure shows as a dash** — that figure could not be calculated, and the reason is on the screen
with the compliance question beside it. It is not a zero, and it must not be treated as one.

**Payroll will not approve** — read the message. It will be one of: the run is a Development
calculation, a figure is unresolved, or you are the person who calculated it.

---

## 18. What this software will not do

- It will not run a live payroll on unverified statutory rules.
- It will not treat an unresolved figure as zero.
- It will not add USD to ZiG, anywhere, including in a journal.
- It will not mark a statutory obligation paid because payroll was approved.
- It will not let one person both capture and approve the same input.
- It will not edit a locked payroll.
- It will not delete an employee or a user who has history.
- It will not store a password in recoverable form, or ship a default one.
- It will not generate a bank payment file, or a ZIMRA or NSSA submission file, because those
  formats could not be obtained from an authoritative source and inventing one would put a guess in
  a file sent to a bank or an authority.
