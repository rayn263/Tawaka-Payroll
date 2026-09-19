# FINAL RELEASE STATUS

**Tawaka Payroll — 19 September 2026**

## RELEASE CANDIDATE — COMPLIANCE UNVERIFIED

Not production-ready, and not claimed to be. Two things stand in the way, neither of them a code
change: **no statutory rule has been checked against its official source**, and **the Windows
application has never been run**. The software enforces the first on itself and refuses to run a
live payroll. The second needs a Windows machine, which this environment is not.

Everything else — the engine, the data, the workflow, the screens, the security, the audit trail —
is built, wired and tested.

---

## What Was Actually Tested

Executed here, on Linux, against real SQLite databases built by the real migrations:

| | |
|---|---|
| **The whole business lifecycle, twice** | Company → users → employee → contract → earnings → project and site → timesheet → overtime → leave → loan → input approval → period → run → snapshot → calculate → explain → review → approve → finalise → obligations → net wages paid → statutory payment → payslip → reports → GL journal → lock → audit → backup. Run independently for **USD** and for **ZiG**, driven by an officer, a manager and an administrator created through the application |
| **Every screen** | All seventeen routes across the ten modules rendered headlessly with bUnit against the real composition root: forms filled, validation triggered, records saved, permissions trimmed, the payroll lifecycle driven from the buttons, and every page checked for leaked exceptions, placeholders and non-numbers |
| **Locking** | With a run locked, the header, a result figure, an earning line, a deduction line, an obligation, the snapshot and a consumed timesheet were each modified and each refused |
| **Reproducibility** | A finalised run, then the employee renamed, the contract superseded at a higher salary, a loan taken, leave booked and a statutory rule replaced — the historical figures, the sealed snapshot, its hash and a fresh recalculation from it all unchanged, while the next period correctly used the new contract |
| **Currency separation** | Attempted and refused: adding USD to ZiG, paying an obligation in the wrong currency, a report section for a currency not in the payroll, a CSV line naming both. A structural test proves no report field could hold a cross-currency total |
| **The obligation lifecycle** | Approve → part payment → second part payment → refused overpayment → settlement → reversal → settle again, with the outstanding balance asserted after every operation |
| **Backup and restore** | Monday backup, Tuesday work, Tuesday backup, restore Monday: the later work gone, the earlier intact, migrations complete, the application opening on it. A folder that only looks like a backup refused without touching the live database |
| **Security** | Hashing, lockout, forced first change, enumeration resistance, inactive accounts, the permission matrix, segregation of duties by permission **and** by identity, and the refusal to disable the last account that can manage users |
| **The audit trail** | Every material action — user creation, contract supersession, calculation, approval, finalisation, statutory payment, payslip issue and re-issue, locking, backup, restore — checked to have left an entry with an actor and a time |
| **A clean install** | An empty file to a working installation: 7 migrations, the statutory baseline, four roles, 38 permissions, both currencies, a generated administrator password, and a live-payroll gate that reports itself blocked |
| **Performance** | 10, 50 and 100 employees. A hundred-employee payroll calculates in 1.6 s, finalises in 0.4 s, reports in 0.2 s and produces a payslip in 0.1 s |

**Not tested, and not claimed:** anything requiring Windows — the WPF window, WebView2, printing,
high-DPI, the clipboard, process lifetime, or the self-contained executable.

---

## Test Results

| | Debug | Release |
|---|---|---|
| Test methods written | **486** across 59 classes | same |
| Cases executed | **696** (a `[Theory]` runs once per `[InlineData]`) | **696** |
| Passed | **695** | **695** |
| Failed | **0** | **0** |
| Skipped | **1** | **1** |
| Build warnings | **0** | **0** |

| Project | Passed | Skipped |
|---|---|---|
| Tawaka.Domain.Tests | 29 | 0 |
| Tawaka.Payroll.Engine.Tests | 234 | 1 |
| Tawaka.Application.Tests | 43 | 0 |
| Tawaka.Infrastructure.Tests | 311 | 0 |
| Tawaka.Ui.Tests | 78 | 0 |

**No base class declares a test**, so nothing is counted twice — checked mechanically across all 59
classes, not assumed. That check exists because the Milestone 5 report said 608 when the honest
figure was 518.

**The one skipped test** is TC-18, part-time NSSA ceiling treatment, skipped with a documented
reason: it depends on compliance questions Q4a and Q22, and an FTE-based ceiling is not an
established rule. It is the only skip in the suite.

---

## Windows Build Status

**The Windows host has never been compiled or run. This is not a claim that it works.**

Three ways to build it here were tried and all three are closed:

| Attempt | Result |
|---|---|
| `dotnet build Tawaka.Payroll.sln -c Release` | `MSB4019` — the Linux SDK ships no `Microsoft.NET.Sdk.WindowsDesktop` folder |
| `-r win-x64 --self-contained`, and `-p:EnableWindowsTargeting=true` | Same failure. That switch supplies targeting packs from NuGet; it cannot supply a missing SDK folder |
| Fetching the Windows SDK to borrow the folder | `builds.dotnet.microsoft.com` blocked by this environment's proxy (403 at CONNECT); NuGet carries that package only at 3.0.0, from the .NET Core 3.0 era |

**What is established:**

- Every other project in `Tawaka.Payroll.sln` builds in Release with **0 warnings**. The single
  error is the WPF host, for the reason above.
- The solution file itself is now valid. It had been malformed since Milestone 1 and failed to
  parse with `MSB5008` before MSBuild read a project — **no Windows build was possible at all**
  until this was found and fixed. A test now guards it.
- The host's wiring is checked against its files, because a project that cannot be compiled cannot
  be checked by a compiler: the project targets `net8.0-windows` with WPF and references everything
  it hosts; `App.xaml` has no `StartupUri`, and startup assigns `App.Services`, migrates and seeds
  **before** the window is created; the root component selector matches an element on the host
  page; the host page loads the framework script and a stylesheet that exists; and the publish
  profile is self-contained win-x64 and untrimmed.
- `ApplicationHighDpiMode` was removed from the project: it is a Windows Forms property that does
  nothing in WPF and read as configuration while having no effect.

**The procedure to close this is `docs/WINDOWS_VALIDATION.md`**, sections A to J.

---

## Security Status

Full statement in `SECURITY.md`. In summary:

| | |
|---|---|
| Passwords | PBKDF2-HMAC-SHA256, 210,000 iterations, per-user salt. **No plaintext anywhere.** No default credential exists in the codebase to become a production default |
| First administrator | Generated, shown once, must be changed at first sign-in |
| Lockout | Five failures, fifteen minutes, clearable by an administrator |
| Enumeration | An unknown user and a wrong password give the same message |
| Segregation of duties | Enforced on a role, across a user's roles **taken together**, and by identity on payroll approval — holding the permission is not enough |
| Administration | Create a user, change their roles, reset a password, disable and re-enable, clear a lockout, read the audit trail. **No superuser bypass**: an administrator cannot approve what they calculated, cannot disable their own account, and cannot remove the last account able to manage users |
| Audit | Append-only, per-field, with actor, time and machine. Password hashes are excluded. Nothing in the application edits or deletes an entry — verified against the source |
| Locking | Enforced at the data layer, so no screen or import can bypass it |
| Company boundary | Every company-owned row carries its company; every list query filters by it |

**Not attempted, deliberately:** encryption at rest, network security (there is no network
surface), multi-user concurrency, two-factor authentication.

---

## Database Status

| | |
|---|---|
| Provider | SQLite, one file in the user's profile |
| Migrations | **7**, applying in order from an empty file, none pending |
| Tables | 73 |
| Money | Scaled integers, never floating point; decimals round-trip exactly |
| Timestamps | Stored UTC-first in a fixed-width sortable form with the offset preserved, so the database can order by them (ADR-040) |
| Foreign keys | Enforced by SQLite itself; an orphan insert is refused |
| Unique constraints | Enforced at the database, not only in the services — a duplicate inserted past the service is refused |
| Required columns | A row without one is refused |
| Deletion | An employee with history cannot be deleted; the record survives |
| Clean install | Verified from an empty file through to a working installation |

---

## Payroll Engine Status

The engine is the single authority for payroll arithmetic, and the code review confirmed it:
**no statutory constant exists anywhere outside the rule seeder**, and no screen, report or payslip
computes a payroll figure. What the screens total is already-persisted figures, within one currency.

Invariants held across the full salary range, including band boundaries and rounding edges: gross
less deductions equals net; totals reconcile to their lines; statutory deductions never exceed the
earnings they are charged on; PAYE and net pay never decrease as income rises; rounding is applied
once, away from zero; the engine is deterministic; every significant figure has a trace entry; and
every statutory figure cites its rule and verification grade.

**Zero is never substituted for unresolved.** A figure the engine cannot produce is absent, named,
and carries the compliance question it depends on. A run with any unresolved figure cannot be
approved.

**A negative net pay is refused rather than published** (ADR-042): a recovery larger than the pay it
comes from names the recovery, leaves net pay absent and blocks approval, rather than flooring at
zero — which would recover less than the loan ledger recorded — or putting a negative on a payslip.

---

## Currency Status

USD and ZiG are separate throughout, and the separation is structural rather than conventional.

- `Money` refuses arithmetic across currencies at the type level.
- Every monetary report section is a `CurrencySection`; a reflection test proves no field on the
  report model could hold a cross-currency total.
- Each currency gets its own GL journal, balancing on its own; there is no combined balance to be
  meaningless.
- Statutory obligations are per authority **per currency**, and a payment in the wrong currency is
  refused.
- A conversion never destroys the original: amount, currency, rate, rate date and rate source are
  all preserved, and a completed conversion does not change when the stored rate later does.
- A payment account may be in a different currency from the pay; it changes no payroll figure.
- A loan in a currency the employee is not paid in is not recovered — and no longer silently: the
  snapshot records it as an input the run did not use, and the report says so.

**With the shipped seed a monthly ZiG payroll cannot be calculated**, because no verified ZWG
monthly table exists to calculate it from. That is compliance question Q29, and the application
does the right thing with it: the figure is absent and named, never `0.00`.

---

## Compliance Status

**No rule in this release is VERIFIED. Not one.** Full statement in `docs/COMPLIANCE_STATUS.md`.

The four statuses describe the **evidence**, never the implementation: VERIFIED (confirmed against
the official source, which is recorded), SUPPORTED (credible secondary sources agree, official
document unread), UNVERIFIED (insufficient or conflicting), and UNRESOLVED — not a rule status but
the runtime outcome when no usable rule exists: the figure is absent and the reason named.

Access to every authoritative Zimbabwean source failed again during this stage, for the sixth time
(`403` at CONNECT to zimra.co.zw, nssa.org.zw and veritaszim.net). **No rule could have been
verified from here, by anyone.** Nothing was substituted, and no rule was promoted because the
application implements it well.

| Q | Blocks live payroll for |
|---|---|
| **Q1** multi-currency PAYE method | Any employee paid in more than one currency |
| **Q6** APWCS assessed rate | Employer cost. External: only NSSA can supply it |
| **Q22** NSSA ceiling on non-monthly pay | Weekly and fortnightly payroll |
| **Q26** PAYE fixed-deduction column | **All PAYE, both currencies.** The critical one |
| **Q29** ZWG monthly PAYE table | ZiG payroll on any non-annual period |
| **Q30** 2026 tables or 2025 carried forward | Verifying any seeded table |
| **Q31** overtime multipliers | Any employee with overtime |
| **Q32** statutory leave entitlements | Any claim to implement statutory leave |
| **Q33** pay divisor | Unpaid leave, salaried overtime, part-period starts and ends |

On Q29 the two claims stay separate: that this environment cannot retrieve a ZWG monthly table is
asserted and evidenced; that no such table exists is **not claimed** and is encoded nowhere.

**A rule can now be verified from the application** — one at a time, with the document recorded,
refused without one, audited, and reversible. Before this stage the only route was a development
CLI switch, so the gate had no key.

---

## Known Limitations

Nothing here is P0 or P1. All are recorded rather than hidden.

| | |
|---|---|
| **Windows validation not performed** | The largest one. The procedure is written and precise; it needs a Windows machine |
| **Role permissions cannot be edited** | The four shipped roles are what an installation has. `RoleService` can change a role's permissions and is tested; no screen exposes it |
| **Leave entitlement adjustment has no screen** | Entitlements are created automatically from the leave type when leave is first requested. Opening balances brought forward from another system, and manual corrections, are supported by `LeaveService.AdjustAsync` but not reachable from a screen |
| **Loan over-recovery approval has no screen** | The rule exists and is tested; recovering more than the outstanding balance cannot be approved from the application |
| **A second holiday calendar cannot be created** | One calendar per company is created by seeding; additional calendars are supported by the service only |
| **Skipped inputs surface on Reports, not on the preview** | An officer sees what a run left out after calculating rather than before approving |
| **Single user at a time** | One person, one database. There is no locking between concurrent users because there are none |
| **No encryption at rest** | Use Windows full-disk encryption |
| **Unused `using` directives were not removed** | Reliable detection needs analyser configuration enabled across every project; the benefit is cosmetic and the instruction was not to churn |
| **The ADR log is one file, not a directory** | `docs/DECISIONS.md` holds all 42 records. Splitting them into 42 files would have created dozens of documents, against the instruction |

---

## Required External Actions

None of these is a code change.

**1. Prove it runs on Windows.** Work through `docs/WINDOWS_VALIDATION.md` sections A–J on a clean
Windows 10 (1809+) or 11 x64 machine. Produce the executable, and check it runs on a second machine
with no SDK. Report failures with the screen and the contents of `logs\`.

**2. Obtain these documents.** From a machine that can reach them:

| # | Document | Answers |
|---|---|---|
| 1 | ZIMRA PAYE tables for tax year 2026 — the published tables, USD and ZWG, every period basis in use, **including the fixed-deduction ("less") column** | Q26, Q29, Q30 |
| 2 | The NSSA contribution notice, including how the monthly ceiling applies to weekly and fortnightly pay, and the insurable-earnings basis | Q22, Q4 |
| 3 | NSSA Form WC50 for this employer, and the assessment notice NSSA returns | Q6 |
| 4 | A ZIMRA statement on multi-currency PAYE: aggregate or separate, which rate, as at which date | Q1 |
| 5 | Labour Act [Chapter 28:01] on overtime and leave, plus the construction NEC agreement (NECCIZ, SI 112 of 2021 and later) | Q31, Q32, Q33 |

**3. Verify each rule in the application.** Statutory → Statutory rules → **Verify**, recording the
document, where in it, and its date. Do the same for leave entitlements and public holidays. The
release stage moves to READY FOR CONTROLLED TESTING on its own when the last one is done.

**4. Run in parallel.** At least two full pay cycles alongside the existing process, reconciling
gross, every deduction, net pay, employer cost and every obligation, employee by employee.
Investigate every difference and record what it turned out to be.

**5. Set the business up properly.** Company profile, currencies, bank accounts, GL mappings, an
account per person, and backups kept somewhere other than the payroll machine.

**6. Decide.** A named person enables live payroll with a recorded reason. The software will
continue to refuse until step 3 is complete; after that, the decision is a person's.

---

## Final Installation Instructions

```powershell
# On Windows, with the .NET 8.0 SDK:
git clone <repository-url> tawaka-payroll
cd tawaka-payroll

dotnet build Tawaka.Payroll.sln -c Release
dotnet test  Tawaka.Payroll.sln -c Release

dotnet publish src/Tawaka.Ui.Desktop -c Release -p:PublishProfile=win-x64
```

Produces `src\Tawaka.Ui.Desktop\bin\Release\publish\win-x64\Tawaka.Payroll.exe` — self-contained,
single file, no .NET runtime needed on the target machine.

**To install:** copy that one file anywhere the user can write. Install Microsoft's WebView2
Evergreen runtime if the machine lacks it. Run it.

**On first start** it creates `%LOCALAPPDATA%\Tawaka Payroll\`, creates and migrates its database,
seeds the statutory baseline and the four roles, and shows a **generated administrator password
once**. Write it down — it cannot be recovered. Sign in as `admin`, change it when prompted, then
follow `USER_GUIDE.md` §1.

**The executable has not been produced here and is not in this repository**, because it cannot be
built in this environment. The command above is the one that produces it.

---

## Recommended Next Step

**Build it on Windows and work through `docs/WINDOWS_VALIDATION.md`.**

That is one task, on one machine, and it is the only thing standing between this and a product
that can be installed. Everything else — the statutory verification, the parallel running, the
go-live decision — is work for the business and its tax advisor, and it can begin the moment
somebody has the documents.

**Do not start another development milestone.** The application is feature-complete for its scope.
What it needs now is a Windows machine and a person with the ZIMRA tables.
