# Tawaka Payroll — operations

Running it: release stages, backups, restoring, locking, the audit trail and demonstration data.
For installing it see `DEPLOYMENT.md`; for using it see `USER_GUIDE.md`.

---

## 1. The release stage

The top bar of every screen shows one of four stages. It is computed from the database, not set by
anybody (ADR-037).

| Stage | What it means | How you leave it |
|---|---|---|
| **DEVELOPMENT** | No company details, or no employees. Demonstration only | Complete the company profile and add employees |
| **COMPLIANCE-UNVERIFIED** | Configured and usable, but statutory rules have never been checked against an authoritative source. Payroll calculates so the workings can be inspected; it cannot be approved | Verify every rule (`USER_GUIDE.md` §2) |
| **READY FOR CONTROLLED TESTING** | Every rule verified. Run payrolls in parallel with your existing process and reconcile | Nothing the software can do. A person decides |
| **LIVE PAYROLL ENABLED** | A recorded decision, with a reason, after parallel running | — |

**This release ships as COMPLIANCE-UNVERIFIED**, and that is the honest position: no statutory
figure in it has been read from ZIMRA, NSSA or a Statutory Instrument.

There is no shortcut. Enabling live payroll is refused while any required rule is unverified, and
requires a recorded reason when it does succeed. The `--verify-rules` switch on the command-line
tool is a **development facility**: it marks every seeded rule verified at once, with no source, for
demonstrations. It must never be used on a database anybody intends to pay people from.

---

## 2. Backups

**Settings → Backup & restore.**

A backup is a consistent copy taken through SQLite's own backup mechanism — not a file copy — plus
a manifest naming the application, the schema version, every applied migration, the time, the person
and the size.

**Take one before every payroll run and before every upgrade.** Keep them off the machine: a backup
on the same disk protects you from a mistake, not from a failure.

Taking a backup is recorded in the audit trail.

## 3. Restoring

Restoring **replaces all current payroll data** and must be confirmed explicitly.

1. The backup is checked first: a folder that is not a backup, or one whose database no longer
   matches its manifest, is refused rather than attempted.
2. The database in use is set aside as `tawaka-payroll.db.replaced-<timestamp>` — never
   overwritten.
3. The backup is copied into place. If that fails, the displaced database is put straight back.
4. **Close and reopen the application.**

The restore is recorded in the audit trail of the restored database, naming when the backup was
taken, by whom, on which schema, and where the displaced database went — the first thing in the
recovered history that says the history was recovered.

A mistaken restore is undone by closing the application, deleting the restored `tawaka-payroll.db`
and renaming the `.replaced-<timestamp>` file back. A backup taken on a newer version of the
software is refused; install that version first.

---

## 4. Locking

Locking a payroll run freezes the run, its result figures, every earning, deduction and
employer-cost line, its statutory obligations, its sealed input snapshot, and the timesheets, leave
and loans it consumed. The protection is in the data layer, not in the screens: an attempt to change
any of it fails wherever it comes from.

Statutory payments are a **later** event and remain possible after the lock — the money still has
to be paid and recorded.

---

## 5. The audit trail

**Administration → Audit.** Append-only and per-field: what changed, from what, to what, when, by
whom, on which machine. Password hashes and salts are excluded.

Covered: employee and contract changes, payroll calculation, approval, finalisation, statutory
payments and reversals, payslip issue and re-issue, locking, corrections, user and permission
changes, and backups and restores.

Filter by date, user, record type, action or free text. The history of any single record is
available from that record. Nothing in the application edits or removes an audit entry.

---

## 6. Demonstration data

For training or evaluation — eight employees across both currencies and every employment type, with
approved time, leave and a loan:

```powershell
dotnet run --project tools/Tawaka.Foundation.Cli -- --demo-data
```

It refuses outright if the database already holds any employee, stamps the installation as
demonstration data, appends "[DEMONSTRATION]" to the company name, creates its payroll period in
development mode, and puts a banner on every screen until the stamp is removed.

**Never seed it into a database you intend to use for real payroll.** Setting a company up normally
does not create demonstration employees.

---

## 7. Health checks

```bash
./foundation-check.sh      # migrations, seed, the live-payroll gate, a worked calculation
./test.sh                  # the whole cross-platform suite
```

`foundation-check.sh` prints which rules are blocking live payroll and why, and ends with what the
installation may currently do.

---

## 8. Day-to-day problems

**An account is locked out** — five failed sign-ins lock an account for fifteen minutes. An
administrator clears it immediately: **Administration → Users → Unlock**.

**Somebody has forgotten their password** — **Reset password** on their row issues a new generated
one, shown once, which they must change at next sign-in.

**A figure shows as a dash** — that figure could not be calculated. The reason is on the screen with
its compliance question. It is not a zero.

**Payroll will not approve** — read the message: a development calculation, an unresolved figure, or
you are the person who calculated it.

**The journal does not balance** — the screen says what is unmapped. Map the missing account in
Settings → Accounting, per currency.

**The disk is full** — the database, the logs and the exports are all under the user's profile. The
error log is at `%LOCALAPPDATA%\Tawaka Payroll\logs\`.
