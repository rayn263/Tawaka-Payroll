# Final QA report — Tawaka Payroll release candidate

**Date:** 17 September 2026
**Scope:** Final QA, Windows validation and release-candidate audit. No new features; defect repair,
validation and packaging only.
**Verdict:** **Release candidate. Not released, and not fit for live payroll** — for two separate
reasons, one of which the software enforces on itself.

---

## 1. Windows runtime result

**NOT PERFORMED. Windows validation could not be carried out in this environment.**

The development environment is Linux. Its .NET SDK does not ship the Windows Desktop targets, so the
WPF host cannot be compiled at all — not merely not run:

```
error MSB4019: The imported project
  ".../Microsoft.NET.Sdk.WindowsDesktop/targets/Microsoft.NET.Sdk.WindowsDesktop.targets"
  was not found.  [src/Tawaka.Ui.Desktop/Tawaka.Ui.Desktop.csproj]

$ ls /usr/lib/dotnet/sdk/8.0.131/Sdks/ | grep -i windows
(nothing)
```

Cross-compiling with `-r win-x64 --self-contained` fails at the same import: the targets file is
missing, not the runtime.

**What this audit did establish about the Windows build, which is not nothing:**

- `Tawaka.Payroll.sln` **had never been buildable on any platform.** It failed to parse with
  `MSB5008` before MSBuild read a single project, because it was hand-edited in Milestone 1 (the
  `dotnet sln add` command refuses a WPF project on Linux) and the edit put the desktop project's
  configuration lines inside a section that cannot hold them, wrote sixteen garbage
  concatenated-GUID lines, mapped Release configurations onto Debug for two test projects, and left
  the desktop project out of `ProjectConfigurationPlatforms` and `NestedProjects` entirely. **This
  was a P0 for Windows delivery and is fixed.** The solution now parses, `dotnet sln add` works
  against it, and every cross-platform project in it builds in Release with zero warnings.
- The only remaining error in `dotnet build Tawaka.Payroll.sln -c Release` is the missing
  WindowsDesktop targets, on the one project that needs them.

**The remaining procedure is `docs/WINDOWS_VALIDATION.md`**, which states that it has not been
performed, shows the evidence for why, separates what headless rendering already established from
what is genuinely unknown, and gives the steps: build, test, publish, start-up, WebView2
initialisation, authentication, all ten modules, the full journey including the locked-payroll
refusals, both currencies, printing, restart, high-DPI, and performance.

**Nothing in this report claims any Windows behaviour has been observed.**

---

## 2. End-to-end UI result

The screens were rendered and exercised, headlessly, for the first time.

A new project, `tests/Tawaka.Ui.Tests`, renders the **real Razor components** with bUnit against the
**real composition root** (`AddTawakaInfrastructure`), the **real migrations** and a **real SQLite
database** — the same wiring the desktop host uses, minus WPF and WebView2. 43 tests.

This is not compilation and not Razor type-checking. It runs the component lifecycle, the router,
data binding and event handlers, and it immediately found a defect that compilation could not see
(§3).

| Covered | Result |
|---|---|
| All seventeen routes across the ten modules, plus tabs | Render, no not-found, no exception |
| Sign-in gate: nothing but the sign-in screen until authenticated | Holds |
| Unknown route | Not-found screen, not a crash |
| Repeated navigation between every module in one session | No failure on re-entry |
| Add employee: every field, both currencies, validation, save, duplicate refusal | Works |
| Employee list: ZiG shown as "ZiG"; Add button hidden without the permission | Works |
| Payroll lifecycle from the buttons: create period, calculate, approve as a second person, finalise, net wages paid, lock | Works; locked run stops offering the actions |
| Segregation of duties from the screen | The calculator's approval is refused |
| Preview shows only stored figures | Every figure on screen equals the persisted value |
| Unresolved shown as unresolved | ZiG payroll with no verified monthly table shows unresolved, not `0.00` |
| Payslip: values, currency label, development watermark present/absent | Works |
| All fourteen report tabs | Render; none grows a section for a currency the payroll does not use |
| Statutory obligations: approve, part-payment, outstanding balance, permission gating | Works |
| User administration: create, password shown once, refusals | Works |

**Not covered, and not claimed:** WPF window behaviour, WebView2 rendering, printing, high-DPI,
keyboard and clipboard behaviour, Windows dark mode, process lifetime. Those are §1.

---

## 3. Payroll calculation result

The engine remains the single authority for payroll arithmetic, and the code review confirmed it:
**no statutory constant exists anywhere outside the rule seeder**, and no screen, report or payslip
computes a payroll figure. What aggregation the screens do is column totals **within one currency
group** of figures the engine already produced.

The calculation itself was exercised end to end in both currencies, from the services and from the
screens, and every figure reconciles to the persisted result.

**One P1 defect was found here, by rendering rather than by testing services:**

> `SQLite does not support expressions of type 'DateTimeOffset' in ORDER BY clauses.`

Twelve queries — the dashboard, the payroll run list, loans, the timesheet approval queue, reports
and the audit trail — ordered by a timestamp column. EF Core's SQLite mapping writes a
`DateTimeOffset` as its *local* date and time followed by the offset, which does not sort
chronologically once two rows carry different offsets, so the provider refuses to translate
`ORDER BY` over it and throws at query time. **Every screen in the application failed on first
render.** Neither the compiler, the Razor type check nor the service-level tests could see it,
because those tests order in memory over lists they have already materialised.

Fixed by `SortableDateTimeOffsetConverter` (ADR-040): the instant is written first, in UTC, in a
fixed-width form, with the original offset appended — `2026-09-17T12:32:00.0000000Z+02:00`. Ordering
the text is exactly ordering the instant, the offset round-trips and no precision is lost. Applied
to every `DateTimeOffset` in the model, because a column it misses is a column the database cannot
order by. The column type is unchanged, so no migration was needed.

---

## 4. USD result

A complete USD payroll runs end to end: contract → earnings → allowance → overtime → unpaid leave →
loan recovery → PAYE → AIDS Levy → NSSA → employer cost → net pay → payslip → obligations →
statutory payment → reports → journal → lock.

Every figure on the payslip and on all fourteen reports reconciles, to the cent, to the stored
payroll result. The stored input snapshot reproduces the result exactly when recalculated.

---

## 5. ZiG result

ZiG is treated identically in structure and separately in substance. A ZiG contract stores ZiG; a
ZiG earning stays a ZiG earning; ZiG obligations are ZiG obligations settled in ZiG.

**With the shipped seed, a monthly ZiG payroll cannot be calculated** — the seed ships a ZWG
*annual* table and no ZWG monthly table, which is compliance question Q29 and is still open. The
application does the right thing with that: the PAYE figure is **absent and named**, shown as a dash
with its compliance question, never as `0.00`. That behaviour is tested from the screen.

With test scaffolding supplying a ZiG monthly table — clearly marked as scaffolding, and no claim
about what ZIMRA publishes — the full ZiG journey runs end to end exactly as USD does.

**Currency separation, tested by trying to break it:**

- A mixed payroll produces two separate sets of figures with nothing summed across them.
- A CSV export keeps each currency in its own table; no exported line names both currencies.
- An obligation cannot be paid in the wrong currency; the attempt leaves the balance untouched.
- Adding a USD total to a ZiG total throws `CurrencyMismatchException` rather than producing a
  number.
- Structurally, **no field on the report model could hold a cross-currency total**: every monetary
  collection is a `CurrencySection<T>`, and a test asserts it by reflection so a future addition
  cannot quietly break the rule.

---

## 6. Payslip result

Rendered and checked against the database. The payslip performs **no calculation**: every figure on
it is one the engine produced and the database holds.

| Check | Result |
|---|---|
| Employee, number, period, pay date, currency label | Correct; ZWG displays as "ZiG" |
| Earnings, deductions, statutory lines, net pay | Equal to the stored result |
| Gross, total deductions, net | Reconcile |
| Unresolved figures | Shown as a dash, never `0.00` |
| Legitimate zeros | Shown as `0.00` |
| Development watermark | Present on a Development payslip, absent on a live one |
| Revision and supersession | A re-issue is a new revision; the superseded one stays on the record |
| Layout | No clipped text, no missing values, in the rendered markup |

Print layout on Windows is §1.

---

## 7. Statutory obligation result

The four states remain independent, and `IsPaid` is derived from unreversed payments alone.

The full lifecycle was run as one continuous sequence with the outstanding balance asserted after
**every** operation: calculated and deducted → approved (still owed in full) → part payment →
second part payment → an overpayment refused and nothing changed → settlement → reversal, which puts
the money back and takes the obligation out of paid → settled again. The reversed payment stays on
the record with its reason; four payments are on the register at the end.

Also confirmed: approving a payroll marks nothing paid; marking net wages paid marks nothing paid;
employer-borne obligations never become employee deductions and carry a zero deducted amount with
`IsDeductionApplicable = false`.

---

## 8. Reporting result

Fourteen report families, each grouped by currency. Every one reconciles to the persisted payroll
result — payslip, register, summary, deductions, employer cost, statutory, project and site labour
cost, employee earnings, PAYE and AIDS Levy, NSSA, statutory payments, inputs, audit, journal.

Filters (currency, department, project, employment type) work and the filter in force is stated on
the report itself. CSV export carries correct headings, rows, currency, dates and totals, with one
table per currency.

**One P2 defect found and fixed here.** A loan in a currency the employee is not paid in is
deliberately not recovered — recovering it would need a conversion nobody has authorised, which is
Q1 — but the instalment was being dropped **without trace**: no deduction, no unresolved item,
nothing on any report. The employee still owed the money and nobody would have found out. The sealed
snapshot now records every input the run left out with its reason, and the Payroll inputs report
shows them, on screen and in the CSV, under a heading that says what it means. The new-loan screen
also defaults to the employee's pay currency and says plainly what choosing another one costs.

---

## 9. General ledger result

Debits equal credits **within each currency**, checked per currency. A mixed-currency payroll
produces one journal per currency and there is no combined balance to be meaningless — the journal
type has no field that could hold one. Configurable mappings work, per amount type and per currency.
An amount with no mapped account is **reported as unmapped** rather than posted to a default.

---

## 10. Backup and restore result

Exercised the way a business uses it: a Monday backup, work, a Tuesday backup, then a restore of
Monday because Tuesday was a mistake.

| Check | Result |
|---|---|
| Backup is a real SQLite backup, not a file copy | Yes |
| Manifest records application, schema version, every applied migration, time, person, size | Yes |
| Restore-earlier: the later change disappears, the earlier data remains | Yes |
| Schema valid after restore: every migration applied, none pending | Yes |
| The application opens on the restored database | Yes |
| Restore without explicit confirmation | Refused |
| A folder that only looks like a backup | Refused; the working database is untouched and no file is displaced |
| A backup whose database no longer matches its manifest | Refused |
| The displaced database is kept as `.replaced-<timestamp>` | Yes, so a mistaken restore is recoverable |

---

## 11. Security result

**A P0 was found here, and it made the release candidate unusable for its purpose.**

The Administration screen listed users read-only, and **nothing in the application could create
one** — no service, no screen. An installation therefore held exactly the administrator generated at
first run. Approval refuses whoever calculated the run, as it must, so that single account could
produce a development calculation and nothing else: no approval, no finalisation, no statutory
obligations, no payment. **The control model the whole product is built on could not be reached.**

Fixed (ADR-041): `UserAdministrationService` creates users, sets roles, resets passwords, disables
and re-enables accounts and clears lockouts, all behind `Users.Manage`, exposed on the Administration
screen. Segregation of duties is now checked across the **union** of a user's roles as well as within
one role. The last account that can manage users cannot be disabled or stripped of the permission,
and nobody can disable their own. Proved end to end: two users created through the application run a
payroll between them, the officer calculating, the manager approving.

| Check | Result |
|---|---|
| Unauthenticated access | Blocked — the root component renders only the sign-in screen |
| Officer cannot do manager-only actions | Enforced |
| Whoever calculates cannot approve | Enforced, and enforced by identity, not by role name |
| Whoever captures cannot approve the same input | Enforced for time, leave and loans |
| An ordinary user cannot change statutory rules | Enforced |
| A locked payroll cannot be edited | Enforced at the data layer, so no screen or import can bypass it |
| Sensitive actions audited | Yes, per field |
| Passwords | PBKDF2-HMAC-SHA256, 210,000 iterations, per-user salt. **No plaintext anywhere**, and the hash and salt are excluded from the audit trail |
| Lockout | Five failures, fifteen minutes; an administrator can clear it |
| First administrator | Password generated, never defaulted, shown once, must be changed at first sign-in |
| Development credentials becoming production defaults | Impossible — there is no default credential in the codebase to become one |
| Company data boundary | Two companies in one database: employees, runs, loans, timesheets, leave, obligations and projects are all scoped. **Two holes found and closed** (§14) |

---

## 12. Audit result

Append-only and per-field: what changed, from what, to what, when, by whom, on which machine.
Password hashes and salts are excluded. Obligation transitions, permission changes, sign-ins,
sign-outs and lock and reopen events are all recorded. The history of a single record is queryable,
and a payroll run's own audit is one of the report tabs.

Actions are attributed to a real person from sign-in onwards; nothing payroll-related is recorded
against an anonymous user, because nothing payroll-related can be reached before signing in.

---

## 13. Database and migration result

From an empty file: seven migrations apply in order, none pending, and the schema matches the model.

| Check | Result |
|---|---|
| Foreign keys enforced by SQLite itself | Yes — `PRAGMA foreign_keys = 1`, and an orphan insert is refused |
| Indexes the application relies on | Present, by name |
| Unique constraints | Enforced at the database, not only in the service — a duplicate inserted past the service is refused |
| Required columns | A row without one is refused |
| Company scoping | Every company-owned row carries its company |
| Deletion behaviour | An employee with history cannot be deleted; the record survives |
| Locking | Enforced at the data layer for the run, its result rows, its obligations, its snapshot and its consumed inputs |
| Money | Scaled integers; decimals round-trip exactly |
| Timestamps | Sortable in SQL, with the offset preserved (ADR-040) |
| The application starts on the migrated database | Yes, including after a restore |

One new migration in this milestone: `20260917170000_PayrollPeriodCodeScopedToCompany`.

---

## 14. Defects found and fixed

| # | Severity | Defect | Fix |
|---|---|---|---|
| 1 | **P0** | `Tawaka.Payroll.sln` had never parsed — `MSB5008` before any project was read. The Windows solution could not have been built by anybody, on any machine, since Milestone 1 | Regenerated deterministically from the existing project GUIDs; no project identity changed |
| 2 | **P0** | No way to create a second user. With one account no payroll can ever be approved, finalised or paid | `UserAdministrationService` and the Administration screen (ADR-041) |
| 3 | **P1** | SQLite cannot order by EF's `DateTimeOffset` mapping. Twelve queries did. **Every screen failed on first render** | `SortableDateTimeOffsetConverter`, applied to every timestamp in the model (ADR-040) |
| 4 | **P1** | `PayrollPeriods.Code` was unique across the whole database rather than within its company, so a second company could not have a period called "2026-09" at all — it failed on insert | Migration scoping the index to `(CompanyId, Code)`, matching every other company-owned code |
| 5 | **P1** | `DashboardService` took no company and counted every employee, run and obligation in the database into one set of figures | Scoped to one company throughout; the screen passes the signed-in user's company |
| 6 | **P2** | An employee whose first contract failed validation was already saved, and the correction was then refused as a duplicate — leaving a person on the payroll with no terms and no way to finish the record | Contract validated before anything is written; `ValidateInitialAsync` shares the rules with `CreateInitialAsync` |
| 7 | **P2** | A loan in a currency the employee is not paid in was silently not recovered: no deduction, no unresolved item, nothing on any report | The snapshot records every skipped input with its reason; the Payroll inputs report shows them on screen and in CSV; the loan screen defaults to the pay currency and warns |
| 8 | **P2** | Three screens started a form in USD rather than the company's configured default currency; the employee list's currency filter was hard-coded | All take the company default; the filter reads the currency table |
| 9 | **P3** | One `CS8602` warning in Release only | Fixed; Release and Debug both build with zero warnings |
| 10 | **P3** | Two parameters passed and never read | Removed |

---

## 15. Remaining issues

**P0: none. P1: none.**

| Severity | Issue | Why it is not fixed here |
|---|---|---|
| **P2** | Windows validation has not been performed (§1) | Impossible in this environment. The procedure is written and precise; it needs a Windows machine |
| **P3** | `RoleService` is reachable only from tests. A role's permissions cannot be edited from any screen, so an installation has the four seeded roles and no more. The segregation-of-duties check it enforces is therefore never triggered in normal use | A role-permissions editor is a new feature. Deleting the service would remove a working guard. Recorded rather than either |
| **P3** | The 100-employee payroll costs about twice as much per employee as the 50-employee one (16ms against 7.5ms), suggesting a mildly super-linear path in calculation | Not material: the whole run is 1.6 seconds. "Do not prematurely optimise" |
| **P3** | Inputs a run skipped are surfaced on the Reports screen, but not on the payroll preview where an officer would see them before approving | The information is no longer lost, which was the defect. Putting it earlier in the workflow is an improvement, not a repair |
| **P3** | The desktop host writes an error log but the application has no structured logging | Nothing sensitive is logged today, and adding a logging framework is out of scope |

---

## 16. Final test count

| | |
|---|---|
| **Test methods written** | **456** |
| **Cases executed** | **653** (a `[Theory]` runs once per `[InlineData]`) |
| **Passed** | **652** |
| **Failed** | **0** |
| **Skipped** | **1** — TC-18, part-time NSSA ceiling treatment, pending Q4a and Q22 |
| **Warnings** | **0**, in Debug and in Release |

| Project | Passing | Skipped |
|---|---|---|
| Domain | 29 | 0 |
| Payroll.Engine | 230 | 1 |
| Application | 43 | 0 |
| Infrastructure | 307 | 0 |
| Ui (headless component rendering) | 43 | 0 |

**No base class declares a test**, so nothing is counted twice. This was checked mechanically across
every test class, not assumed — it is how the Milestone 5 report came to say 608 when the honest
figure was 518.

| Build | Result |
|---|---|
| `Tawaka.Payroll.Core.sln`, Debug and Release | Succeeded, 0 warnings, 0 errors |
| `Tawaka.Payroll.sln`, Release | Every project succeeds **except** `Tawaka.Ui.Desktop`, which fails with `MSB4019` because the Linux SDK has no WindowsDesktop targets |
| Migrations | Seven, all apply from zero, none pending |
| Foundation check | Runs; reports **LIVE PAYROLL BLOCKED** with four named unverified or missing rules, and "payroll may run in DEVELOPMENT mode only" |
| Windows runtime | **Not run. Not attempted beyond the build, because the build is impossible here** |

---

## 17. Compliance status

**No rule in this release is VERIFIED. Not one.**

The four statuses are kept strictly apart, and describe the evidence rather than the implementation:
VERIFIED (confirmed against the official source, which is recorded), SUPPORTED (credible secondary
sources agree, official document unread), UNVERIFIED (insufficient or conflicting), and UNRESOLVED —
which is not a rule status at all but the runtime outcome when the engine has no usable rule: the
figure is absent and the reason named, never a zero.

Access to every authoritative Zimbabwean source failed again during this audit, for the fifth time:

```
CONNECT www.zimra.co.zw:443    → 403  (gateway policy denial)
CONNECT www.nssa.org.zw:443    → 403
CONNECT www.veritaszim.net:443 → 403
```

**No rule could have been verified from here, by anyone.** Nothing was substituted in the meantime,
and no rule was promoted because the application implements it.

`docs/COMPLIANCE_STATUS.md` states each of Q1, Q6, Q22, Q26, Q29, Q30, Q31, Q32 and Q33 in the
required five parts. In summary:

| Q | Blocks live payroll | Blocks what, specifically |
|---|---|---|
| **Q1** multi-currency PAYE method | Yes | Any employee paid in more than one currency |
| **Q6** APWCS assessed rate | Yes | Employer cost. External: only NSSA can supply it |
| **Q22** NSSA ceiling on non-monthly pay | Yes | Weekly and fortnightly payroll |
| **Q26** PAYE fixed-deduction column | **Yes, universally** | All PAYE, both currencies. The critical one |
| **Q29** ZWG monthly PAYE table | Yes | ZiG payroll on any non-annual period |
| **Q30** 2026 tables or 2025 carried forward | Yes, indirectly | Verifying any seeded table |
| **Q31** overtime multipliers | Yes | Any employee with overtime |
| **Q32** statutory leave entitlements | Partly | Any claim to implement statutory leave; unpaid leave via Q33 |
| **Q33** pay divisor | Yes | Unpaid leave, salaried overtime, mid-period start or end |

On Q29, the two claims stay separate, as instructed: **(a)** this environment cannot retrieve an
authoritative ZWG monthly table is asserted and evidenced; **(b)** no such table exists is **not
claimed**, and is encoded nowhere.

---

## 18. Release mode

**COMPLIANCE-UNVERIFIED**, computed from the database and not settable by anybody.

`ReleaseReadinessService.SetLivePayrollAsync` **refuses** while any required rule is unverified, and
requires a recorded reason even when it succeeds. That refusal is tested. There is no screen, no
setting and no command that moves this installation to LIVE PAYROLL ENABLED today.

The stage appears in the top bar of every screen and is explained in full on
**Statutory → Compliance status**. What it means in practice: a payroll calculates so the workings
can be inspected, every figure it cannot compute is absent and named, a payslip carries a
**DEVELOPMENT — NOT FOR STATUTORY USE** watermark, and the run cannot be approved — so it cannot be
finalised, cannot create obligations, and cannot be paid.

---

## 19. Installation

Full detail in `DEPLOYMENT.md` and `USER_GUIDE.md`. In short:

1. **Requirements** — Windows 10 (1809+) or 11, x64; Microsoft's WebView2 Evergreen runtime; about
   200 MB of disk; no administrator rights.
2. **Install** — copy `Tawaka.Payroll.exe` anywhere the user can write, and run it. No installer, no
   registry entries. Uninstall by deleting the folder and `%LOCALAPPDATA%\Tawaka Payroll\`.
3. **Build it yourself** — .NET 8.0 SDK, then:
   ```powershell
   dotnet build Tawaka.Payroll.sln -c Release
   dotnet publish src/Tawaka.Ui.Desktop -c Release -r win-x64 --self-contained true `
     -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
   ```
4. **First run** — the database is created and migrated, the baseline is seeded, and a **generated
   administrator password** is shown once. Write it down; it cannot be recovered. Sign in as `admin`
   and change it when prompted. There is no default password.
5. **Set up** — company profile, currencies, bank accounts, then **Administration → Users**: an
   account per person. **An installation with only the administrator cannot run a payroll**, because
   approval refuses whoever calculated the run.
6. **Back up before every payroll run**, to somewhere other than that machine.

### Release-candidate artefacts

| Artefact | Status |
|---|---|
| Source, fully building and tested cross-platform | **Produced** |
| Database and migration package (seven migrations, applied automatically at startup) | **Produced** |
| Documentation: release guide, compliance status, Windows validation procedure, architecture, decisions, schema, testing | **Produced** |
| Windows build and run instructions | **Produced** |
| Backup and restore instructions | **Produced** |
| Test report | **Produced** (§16) |
| Compliance status report | **Produced** (§17, `docs/COMPLIANCE_STATUS.md`) |
| **Self-contained Windows executable** | **NOT PRODUCED.** It cannot be built in this environment (§1). The exact command is in `DEPLOYMENT.md` §3 and must be run on Windows |

---

## 20. Exact remaining actions before live payroll

In order. None of them is a code change.

**A. Prove it runs on Windows.** Work through `docs/WINDOWS_VALIDATION.md` on a clean Windows
machine, sections A to I, recording every result. Produce the self-contained executable (§A5) and
check it runs on a second machine with no SDK (§A6). Report any failure with the screen it happened
on and the contents of `logs\`. **Until this is done there is no release, only a candidate.**

**B. Get the documents.** From a machine that can reach them:

1. ZIMRA PAYE tables for tax year 2026 — the official published tables — for USD and ZWG, at every
   period basis in use, **including the fixed-deduction ("less") column** (Q26, Q29, Q30).
2. The NSSA contribution notice, including how the monthly ceiling applies to weekly and fortnightly
   pay (Q22), and the insurable-earnings basis.
3. NSSA Form WC50 for this employer, and the assessment notice NSSA returns, giving the industry
   classification and the APWCS rate (Q6).
4. A ZIMRA statement on multi-currency PAYE: aggregate or separate, which rate, as at which date
   (Q1).
5. The Labour Act [Chapter 28:01] on overtime and leave, plus the construction NEC agreement
   (NECCIZ, SI 112 of 2021 and later) (Q31, Q32, Q33).

**C. Verify each rule in the application.** For every rule in **Statutory → Statutory rules**,
compare every figure against the document, correct what differs, record the source and its date, and
mark it **Verified**. Do the same for leave entitlements and the holiday calendar. The release stage
moves to READY FOR CONTROLLED TESTING on its own when the last one is done.

**D. Run in parallel.** At least two full pay cycles alongside the existing process. Reconcile
gross, every deduction, net pay, employer cost and every statutory obligation, employee by employee.
Investigate every difference, however small, and record what it turned out to be.

**E. Set up the business properly.** Company profile, currencies, bank accounts, GL mappings, an
account for each person with the right role, and a backup routine that puts backups somewhere other
than the payroll machine.

**F. Decide.** A named person enables live payroll with a recorded reason. The software will
continue to refuse until step C is complete, and after that the decision is a person's, not the
software's.

---

**Nothing in this release will tell an employer that a tax has been paid when it has not, or produce
a figure it cannot account for. What it will not yet do is run on Windows, because nobody has been
able to try.**
