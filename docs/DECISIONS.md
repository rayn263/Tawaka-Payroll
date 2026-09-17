# Architecture Decision Record

Append-only. Each decision keeps its number forever. Superseding a decision means adding a new
entry that references the old one, never editing or deleting history.

Status values: `Proposed` · `Accepted` · `Superseded by ADR-nnn` · `Rejected`

---

### ADR-001 — Windows desktop application on .NET 8 with a Blazor Hybrid UI
**Status:** Proposed
**Context:** A Zimbabwean SME needs payroll that works without reliable internet, looks like
commercial accounting software, and may later move to a server or the cloud.
**Decision:** WPF shell (`net8.0-windows`) hosting Blazor components via WebView2. Domain and
engine are plain .NET and UI-agnostic.
**Consequences:** Modern styling without fighting XAML; UI portable to ASP.NET Core later; adds a
WebView2 runtime dependency, which is present on current Windows and bundled by the installer.

### ADR-002 — Money is a value object; no bare decimals, no floats
**Status:** Proposed
**Decision:** `Money { decimal Amount; CurrencyCode Currency; }`. Cross-currency arithmetic throws.
Persisted as a scaled integer on SQLite, `DECIMAL(18,4)` elsewhere.
**Consequences:** Whole classes of currency-mixing bugs become compile-time or runtime failures
rather than wrong payslips. Slightly more ceremony in code.

### ADR-003 — Every statutory value is a dated, versioned rule row
**Status:** Proposed
**Decision:** No statutory constant in code. Rules carry `EffectiveFrom`/`EffectiveTo`, currency,
status and source reference. A missing rule is a blocking error, never a default.
**Consequences:** Legislative change is configuration, not a release. Historical payroll stays
correct. Requires disciplined seeding and a verification workflow.

### ADR-004 — Payroll runs snapshot their rules and exchange rates
**Status:** Proposed
**Decision:** On calculation, the resolved rule set and rates are persisted with the run by FK and
as immutable JSON.
**Consequences:** Reproducible history; later rule edits cannot rewrite past payroll. Costs some
storage per run — a price worth paying.

### ADR-005 — The calculation engine is pure and has no database access
**Status:** Proposed
**Decision:** Engine input is an immutable snapshot; output is a result plus a full trace. No I/O,
no clock, no current user.
**Consequences:** Exhaustively testable, deterministic and auditable; the single most important
decision for correctness.

### ADR-006 — Audit and period locking are enforced by EF Core interceptors
**Status:** Proposed
**Decision:** A `SaveChanges` interceptor writes audit rows and refuses writes to locked periods.
**Consequences:** Cannot be bypassed by a screen or a future import that forgets to check.

### ADR-007 — Statutory payment status is a four-state machine backed by payment records
**Status:** Proposed
**Decision:** CALCULATED, DEDUCTED, APPROVED, PAID are independent, separately permissioned flags.
`IsPaid` can only be set by the existence of a `StatutoryPayments` row with date, method and
reference. Partial payments supported.
**Consequences:** The system structurally cannot misrepresent compliance. This is a deliberate
constraint on the software, not merely a UI convention.

### ADR-008 — Multi-currency tax methodology is a configurable, advisor-signed strategy
**Status:** Proposed
**Context:** Secondary sources contradict each other on whether USD and ZiG earnings are aggregated
and converted or taxed separately (see `COMPLIANCE_ZIMBABWE.md` §3).
**Decision:** Model it as a versioned `CurrencyTaxStrategies` rule recorded on every run, seeded to
the more conservative aggregate method and flagged `Unverified` until an advisor signs off.
**Consequences:** No methodology is invented. If the interpretation changes, affected runs are
identifiable and can be recalculated deliberately.

### ADR-009 — QuestPDF licensing must be confirmed
**Status:** Proposed
**Decision:** Payslips render through the Blazor template to PDF via WebView2 (no third-party
licence). QuestPDF is used only for fixed-layout statutory forms; confirm Community licence
eligibility or budget for a commercial licence before it enters the build.

### ADR-010 — Multi-company structure from day one, single-company UI until Phase 3
**Status:** Proposed
**Decision:** Every scoped table carries `CompanyId` now; the interface exposes one company until
Phase 3.
**Consequences:** Avoids a painful migration later at almost no present cost.

### ADR-011 — Names are frozen once established
**Status:** Proposed
**Decision:** Table, column, entity, enum and public engine type names change only via a new ADR.
Additive change is always preferred to renaming.
**Consequences:** Two AI assistants and a human can work on this codebase without silently
breaking each other.

### ADR-012 — Verification gate: unverified statutory rules cannot run live payroll
**Status:** Proposed
**Context:** Every Zimbabwean official domain (ZIMRA, NSSA, RBZ, Treasury, Parliament, ZIMDEF,
Veritas, ZimLII) returns HTTP 403 from this environment's egress policy, so no statutory rule could
be confirmed against its primary source. Research was possible only through search-index snippets.
**Decision:** Four confidence grades (🟢 VERIFIED / 🟡+ SUPPORTED-official-text / 🟡 SUPPORTED-
professional-consensus / 🔴 UNVERIFIED) are stored on every rule row. The engine runs in LIVE mode
only on 🟢 rules; anything lower raises a blocking error naming the rule and its remedy. TEST mode
permits lower grades but watermarks every output `TEST — NOT FOR STATUTORY USE`, creates no
statutory obligations, and cannot finalise or lock a run.
**Consequences:** The system cannot produce a live payroll until the verification checklist in
`ZIMBABWE_PAYROLL_COMPLIANCE_SPEC_V1.md` §26 is worked through. That is intentional: a payroll
system that silently computes on unverified rates is worse than one that refuses.

### ADR-013 — Official period tables only; no derived tax tables
**Status:** Proposed
**Context:** ZIMRA publishes daily, weekly, fortnightly, monthly and annual tables for both
currencies, in `gross × rate − fixed deduction` form.
**Decision:** `TaxRules.PeriodBasis` selects strictly on the employee's payment frequency, and
`TaxBrackets.FixedDeductionAmount` stores the official "less" column. The engine never derives a
period table from another (no monthly ÷ 4 or ÷ 4.33, no monthly ÷ 2 for fortnightly). A missing
table is a blocking error.
**Consequences:** Results reconcile line-for-line with ZIMRA's own tables. Weekly payroll cannot
run until the weekly table is loaded — correct, given site staff are commonly weekly-paid.

### ADR-014 — The system never makes a legal employment determination
**Status:** Proposed
**Context:** Labour Act s.12(3) deems a casual worker to be on a contract without limit of time
once engagement exceeds six weeks in any four consecutive months. The deeming operates by law,
regardless of the contract label or what the payroll system records.
**Decision:** `EmploymentType` is an administrative payroll classification only. The engine tracks
the rolling four-month engagement window and warns before the threshold, but never changes the
employment type, leave accrual or NSSA treatment by itself.
**Consequences:** The business is told what it needs to know; the legal determination stays with
people who can make it.

### ADR-015 — Two solution files, so the cross-platform core always builds
**Status:** Accepted (Milestone 1)
**Context:** The desktop shell targets `net8.0-windows` and cannot build on Linux, but the domain,
engine, application, infrastructure and all tests can and should.
**Decision:** `Tawaka.Payroll.Core.sln` contains everything except the desktop shell and is what
`build.sh` and `test.sh` use. `Tawaka.Payroll.sln` adds the desktop shell for Windows work.
**Consequences:** CI and non-Windows contributors get a green build and a full test run. The
desktop project must be added to the Windows solution by hand if recreated, because
`dotnet sln add` refuses it where the WindowsDesktop SDK is absent.

### ADR-016 — xUnit assertions only; no FluentAssertions
**Status:** Accepted (Milestone 1)
**Context:** FluentAssertions changed licence at version 8: commercial use now requires a paid
licence. This is a commercial payroll product.
**Decision:** Use the assertions built into xUnit. No assertion library dependency.
**Consequences:** Slightly more verbose assertions, and no licensing exposure. Same reasoning as
the QuestPDF note in ADR-009 — check the licence before the dependency, not after.

### ADR-017 — Statutory rules use table-per-type mapping
**Status:** Accepted (Milestone 1)
**Context:** The documented schema names a separate table per rule kind, but all rules share
identity, effective dating, verification metadata and source provenance.
**Decision:** `StatutoryRule` is mapped table-per-type: shared columns in `StatutoryRules`, each
rule kind in its own table (`TaxRules`, `NssaRules`, …), preserving the documented names.
**Consequences:** Shared behaviour lives in one place and the documented table names hold. Reads
join across two tables, which is immaterial at this data volume.

### ADR-018 — Verification is not the only gate: incomplete configuration blocks too
**Status:** Accepted (Milestone 1)
**Context:** A rule can be correct in its values yet unusable. A verified NSSA ceiling with no rule
for applying it to weekly payroll (Q22) is one; an aggregate multi-currency strategy without
advisor sign-off (Q1) is another.
**Decision:** The resolver returns `IncompleteConfiguration` for such rules, naming the specific
compliance question, even when the rule is marked Verified.
**Consequences:** Verification cannot be used to wave through a rule whose application method is
still unknown.

### ADR-019 — Screens live in a Razor class library, not in the Windows host
**Status:** Accepted (Milestone 2)
**Context:** Putting screens in the WPF project made the entire user interface uncompilable and
unverifiable outside Windows, which is how Milestone 1 shipped a UI scaffold nobody could build.
**Decision:** All Razor components live in `Tawaka.Ui.Shared`, a Razor class library targeting
`net8.0`. `Tawaka.Ui.Desktop` is reduced to a host: a window, a `BlazorWebView` and startup wiring.
**Consequences:** The whole UI compiles and is verified on any platform and in CI; only the thin
host remains Windows-only. It also makes the Phase 3 web version a hosting change rather than a
rewrite, since the same components can be served by ASP.NET Core.

### ADR-020 — The application layer works against DbSets, not a repository per entity
**Status:** Accepted (Milestone 2)
**Context:** A repository and unit-of-work wrapper for ~30 entities would add several thousand
lines of pass-through code for no behavioural gain.
**Decision:** `IPayrollDataContext` in the application layer exposes EF Core `DbSet`s;
`PayrollDbContext` implements it. The application layer still knows nothing about SQLite,
connection strings, migrations or interceptors.
**Consequences:** Far less ceremony, and services remain testable against a real SQLite database.
The boundary that actually matters is untouched: `Tawaka.Payroll.Engine` still has no data access
at all (ADR-005).

### ADR-021 — Statutory treatment of earnings and deductions is graded like a statutory rule
**Status:** Accepted (Milestone 2)
**Context:** Whether a housing allowance is taxable or NSSA-applicable is as much a statutory
question as a tax bracket, and the evidence for it is just as uneven.
**Decision:** `EarningType` and `DeductionType` carry `TreatmentVerificationStatus`,
`TreatmentSource` and `TreatmentNotes`. An assumed treatment is visible, and will block live
payroll in the same way an unverified tax table does.
**Consequences:** "Is this allowance taxable?" is always answerable with a source and a confidence,
rather than being an invisible assumption inside the calculation engine.

### ADR-022 — The first administrator password is generated, never defaulted
**Status:** Accepted (Milestone 2)
**Context:** A shipped default password is a shipped vulnerability.
**Decision:** First-run seeding creates `admin` with a randomly generated 16-character password,
returned to the caller exactly once for display, stored only as a PBKDF2 hash, with
`MustChangePassword` set.
**Consequences:** No known-credential window. The password cannot be recovered if lost at first
run — the account must be reset instead, which is the correct trade.

### ADR-023 — The workflow lives on the payroll run, not the calendar period
**Status:** Accepted (Milestone 3)
**Context:** The seven-state lifecycle (Draft → Locked) has to attach to something, and a period
may legitimately need more than one processing: a normal run, then a supplementary or a correction.
**Decision:** `PayrollPeriod` remains the calendar period (company, dates, frequency, its own
Open/Closed/Locked state). `PayrollRun` carries the seven-state workflow, the actor on each
transition, the engine version and the rule snapshot.
**Consequences:** A correction run does not disturb the original. The period-level lock still gates
everything inside it, and both are `ILockable`, so the lock interceptor covers both.

### ADR-024 — Unresolved is not zero
**Status:** Accepted (Milestone 3)
**Context:** The cheapest way to make a payroll screen look finished is to treat a missing rule as
a nil deduction. It is also the most dangerous: the payslip looks complete and is wrong.
**Decision:** Money fields on `PayrollResult` are nullable. A figure that could not be produced is
null and carries an `UnresolvedItem` naming the rule, its grade, the compliance question and the
remedy. A legitimate zero is `Money.Zero` and displays as 0.00.
**Consequences:** The preview shows "—" for unresolved and "0.00" for a real zero, and a run with
any unresolved figure cannot be approved.

### ADR-025 — One rounding policy for the whole engine
**Status:** Accepted (Milestone 3)
**Context:** Rounding scattered across calculation classes is how payrolls develop cent-level
discrepancies nobody can explain.
**Decision:** A single `RoundingPolicy` — two decimal places, away from zero, intermediate tax
steps unrounded — is carried on the snapshot and used by every stage. Earnings are rounded to
currency precision on entry so that lines and totals reconcile, and sums of rounded values are
never re-rounded. Every rounding is recorded in the trace.
**Consequences:** Totals always equal the sum of their lines. Away-from-zero is used rather than
banker's rounding because half-to-even drifts systematically in the employer's favour across a
workforce.

### ADR-026 — The user interface never calculates
**Status:** Accepted (Milestone 3)
**Context:** A screen that computes "just the total" is how two sources of truth appear.
**Decision:** Every figure displayed comes from a `PayrollResult` produced by `PayrollCalculator`
and persisted to the run. The preview aggregates and filters stored results; it performs no payroll
arithmetic.
**Consequences:** What the user sees is what was calculated, traced and stored.

### ADR-027 — A statutory obligation has four independent states, and paid is derived
**Status:** Accepted (Milestone 4)
**Context:** A single `Status` column forces a real position — *calculated, withheld from the
employee, authorised by the director, not yet paid to ZIMRA* — into one word, and the word chosen
is usually the optimistic one. The brief is explicit that the system must never say a tax was paid
when it was not.
**Decision:** `StatutoryObligation` carries four independent flags — `IsCalculated`, `IsDeducted`,
`IsApproved` — each with its own actor and timestamp, plus `IsDeductionApplicable` for
employer-borne obligations where nothing is withheld. **`IsPaid` is not a flag.** It is derived:
an obligation is paid when the sum of its unreversed `StatutoryPayment` rows covers the amount due.
The displayed `StatutoryObligationStatus` is computed from the four, plus the due date.
**Consequences:** There is no code path that can mark an obligation paid without a payment carrying
a date, method, amount, currency and reference. Approving a payroll run sets nothing beyond
approval. A reversal retains the payment row with its reason, so the balance restores without the
history disappearing. Employer-borne obligations show `n/a` for deduction rather than a misleading
"no".

### ADR-028 — The payslip renders; it never recalculates
**Status:** Accepted (Milestone 4)
**Context:** ADR-026 established that the UI does not calculate. The payslip is where that rule is
most tempting to break, because a payslip is the one document an employee actually checks, and a
"quick total" in the view is easy to write.
**Decision:** `PayslipBuilder` assembles a `PayslipDocument` from persisted `PayrollRunEmployee`
rows, their earning, deduction and employer-cost lines, and their `UnresolvedItem` rows. It
performs no payroll arithmetic. Each figure is a `PayslipAmount` in one of three states —
`Calculated`, `Zero` or `Unresolved` — and `Unresolved` renders as `—`, never as `0.00`.
Configured statutory categories appear even when zero, so their absence is visible.
**Consequences:** A payslip cannot disagree with the run it came from; the reconciliation test
compares payslip totals against the stored totals directly. A development-mode payslip is
watermarked and `CanBeIssued` is false. Re-issuing after a correction creates a new revision and
supersedes the old one rather than overwriting it.

### ADR-029 — Every report groups by currency before it sums anything
**Status:** Accepted (Milestone 4)
**Context:** A payroll paying some staff in USD and some in ZiG has no meaningful combined total.
A single "total payroll" figure that added the two would be wrong in a way that looks authoritative.
**Decision:** Every report method in `PayrollReportService` begins `GroupBy(e => e.CurrencyCode)`
and returns `CurrencySection<T>` — a currency, its rows and its own totals. There is no field
anywhere in `PayrollReports.cs` that holds a cross-currency total. The currency summary report
places the currencies side by side and deliberately has no combined row.
**Consequences:** A consolidated figure can only ever be produced deliberately, and when it is, it
must state the rate, its date and both original totals. `Reports_never_sum_across_currencies`
asserts this structurally rather than by inspecting one report.

### ADR-030 — A locked run's result rows are locked with it
**Status:** Accepted (Milestone 4)
**Context:** `PeriodLockInterceptor` guarded `ILockable` entities, which is the run and the period.
The figures, though, live in `PayrollRunEmployee` and its lines, and those carry no status of their
own — so a locked run's header was frozen while the numbers it was supposed to freeze could still
be edited by any code that had a `DbContext`. The Milestone 4 acceptance criterion says a locked
run is immutable *at the data layer*, and it was not.
**Decision:** Result rows are marked `IPayrollResultRow`. On save, the interceptor resolves the
owning run for any changed `PayrollRunEmployee` or result row and refuses the write when that run
is `Locked`. Statutory obligations and payments are deliberately **not** marked: paying an
authority is a later event and must keep working after the run is locked.
**Consequences:** Tampering with a locked payroll throws `PeriodLockedException` whichever row is
touched. An unlocked run stays editable, which is what the Review stage is for. The guard reads the
change tracker, so bulk `ExecuteUpdate`/`ExecuteDelete` operations would bypass it; none exist
against payroll data, and any future one must check the lock itself.

### ADR-031 — Time, leave and loans record quantities; the engine prices them
**Status:** Accepted (Milestone 5)
**Context:** A timesheet screen that multiplies hours by a rate is the easiest thing in the world to
write, and it creates a second payroll engine — one with no trace, no rule versioning and no
rounding policy. The same is true of a leave module that works out what an unpaid day costs.
**Decision:** Timesheets, leave requests and loans record **quantities and approvals only**: hours,
days, categories, instalments, who approved what and when. Every conversion into money happens in
`TimeAndAbsenceCalculator`, inside the pure engine, from the snapshot. Overtime is priced by a dated
`OvertimeRule`; a salary becomes a daily or hourly rate through a dated `PayDivisorRule`; an unpaid
leave day is docked at that daily rate.
**Consequences:** There is exactly one implementation of "what is an hour of Sunday overtime worth",
and it is traced like every other figure. A category with no established multiplier leaves the
figure unresolved rather than paying plain time. The one arithmetic that stays outside the engine is
capping a loan deduction at the outstanding balance, which is a fact about the loan ledger rather
than about pay — and the snapshot records the balance it was capped against so the deduction stays
explicable.

### ADR-032 — An unestablished leave entitlement is undetermined, not zero
**Status:** Accepted (Milestone 5)
**Context:** Zimbabwe's statutory leave entitlements come from the Labour Act and from NEC
collective bargaining agreements, neither of which could be read from this environment. The
tempting move is to ship "22 days annual leave" because that is what most people say.
**Decision:** `LeaveType.EntitlementDays` is **nullable** and every seeded type has null, graded
Unverified and tagged with compliance question Q32. `LeaveEntitlement.BalanceDays` is null whenever
the entitlement is null, and the screens render "—". Days *taken* are always exact, because those
come from approved requests rather than from a statute.
**Consequences:** An employer sees "—" until they record their own entitlement with its source,
which is a thing they can actually do. This is ADR-024 applied to leave: the same refusal to let an
unknown quantity wear the clothes of a known one.

### ADR-033 — Public holidays are captured data, never compiled-in
**Status:** Accepted (Milestone 5)
**Context:** Zimbabwe's public holidays are set by the Public Holidays and Prohibition of Business
Act and by presidential proclamation, and the proclaimed dates move from year to year. A list
compiled into the application would be wrong within a year, and wrong silently — payroll would pay
holiday rates on a day that was not a holiday.
**Decision:** `HolidayCalendar` and `PublicHoliday` are data. The seeder creates an **empty**
default calendar and not one holiday. Each captured public holiday starts Unverified and carries a
source; verifying one needs the same permission and the same citation as verifying a tax rule. A
company holiday needs no external evidence, which is what `HolidayKind` distinguishes. Removing a
holiday deactivates it rather than deleting it. A payroll run freezes the calendar identity and the
holiday dates it used into its snapshot.
**Consequences:** The calendar starts empty, which looks unhelpful and is honest. A completed run
can always say which calendar told it a day was a holiday.

### ADR-034 — A loan balance is the ledger, never a stored figure
**Status:** Accepted (Milestone 5)
**Context:** A stored balance beside a transaction ledger is two sources of truth for one number.
They disagree eventually, and when they do nobody can say which is right — least of all the
employee whose money it is.
**Decision:** `EmployeeLoan` stores no balance. `Outstanding` is derived: advances plus interest
less repayments, counting only rows that have not been reversed and excluding reversal rows
themselves. A repayment above the outstanding balance is refused unless over-recovery has been
approved for that loan, by a named person, with a reason. Reversals keep the original row.
**Consequences:** The balance cannot drift. The rule that "a deduction may not exceed what is owed"
has one place to live, and breaking it requires a deliberate, attributed act.

### ADR-035 — The input snapshot is stored, hashed and sealed
**Status:** Accepted (Milestone 5)
**Context:** Milestone 3 made the snapshot immutable in memory, which was sufficient while its
inputs were contracts and dated rules — both reproducible from history. Approved time, leave and a
loan balance are not: recalculating September in March would find a smaller loan balance and
produce a different, equally defensible figure.
**Decision:** Every calculation serialises its snapshot to `PayrollInputSnapshots` with a SHA-256
hash and a seal timestamp, and the approved inputs it consumed are also written relationally to
`PayrollRunInputSources`. Reading a snapshot whose content no longer matches its hash throws rather
than returning it. Serialisation is deterministic, and `Money` and `CurrencyCode` have explicit
converters — without them the currency round-trips to nothing, which would be worse than not
storing the snapshot at all.
**Consequences:** A completed run reproduces against the inputs it actually had, and can name them.
"Which runs consumed this timesheet?" is a query rather than an archaeology exercise. The cost is a
JSON document per employee per run, which is cheap against the alternative of not being able to
answer a dispute.

### ADR-036 — One accounting journal per currency, and unmapped amounts are reported
**Status:** Accepted (Final release)
**Context:** Payroll has to reach the general ledger. The temptation is a single journal with a
currency column, and a default "suspense" account for anything unmapped.
**Decision:** `JournalExportService` produces one `PayrollJournal` per currency, built from
persisted results only. `GlAccountMapping` is keyed on (company, amount type, **currency**), so a
USD wages account and a ZiG wages account are separate rows. An amount with no mapped account is
listed in `Unmapped` and left off the journal entirely.
**Consequences:** A journal balances within its own currency or it is reported as out of balance;
it can never balance by mixing two. An unmapped amount is a visible gap an accountant fixes in
Settings, rather than a suspense posting that reaches the trial balance and stays there.

### ADR-037 — The release stage is observed, not asserted
**Status:** Accepted (Final release)
**Context:** "Is this system ready to run a real payroll?" had no answer in the application. The
compliance position lived in a Markdown file, and the payroll mode badge said only
DEVELOPMENT or LIVE.
**Decision:** `ReleaseReadinessService` computes one of four stages — DEVELOPMENT,
COMPLIANCE-UNVERIFIED, READY FOR CONTROLLED TESTING, LIVE PAYROLL ENABLED — from the database:
whether the company is configured, whether employees exist, and how many statutory rules remain
unverified. Only the final step is a stored decision, it requires a recorded reason, and it is
**refused** while any rule is unverified. The stage is shown in the top bar of every screen and
explained in full on Statutory → Compliance status.
**Consequences:** Nobody can reach for a switch to make a late payroll go live. The software can
take itself as far as "ready for controlled testing" and no further; going live is a person's
decision after parallel running, recorded with their reason.

### ADR-038 — Demonstration data is explicit, refusing and stamped
**Status:** Accepted (Final release)
**Context:** A worked example is genuinely useful for training and evaluation, and genuinely
dangerous if it reaches a real company's database.
**Decision:** `DemoDataSeeder` never runs on its own — it is invoked explicitly — refuses outright
if any employee already exists, stamps the installation with `Data.IsDemonstration`, appends
"[DEMONSTRATION]" to the company name, and creates its payroll period in Development mode. Every
screen carries a banner while that stamp is present. It seeds no public holiday, because a holiday
is a claim about Zimbabwean law that a seeder is in no position to make.
**Consequences:** The example can be shown to a business without any chance of it becoming their
payroll, and the guard is a test, not a convention.

### ADR-039 — CSV first for export, and the browser's print dialogue for PDF
**Status:** Accepted (Final release)
**Context:** Reports and payslips have to leave the system. PDF generation means a library —
QuestPDF, iText, a Chromium print pipeline — each bringing a dependency, a licence and a rendering
surface with its own failure modes, in a release whose priority is consolidation.
**Decision:** Every report exports to CSV, written to `Documents\Tawaka Payroll\Exports`. It opens
in every spreadsheet an accountant owns, carries no formatting to go wrong, and needs no
dependency. Payslips and reports print through `window.print()` against a print stylesheet, which
produces a correct A4 document and a PDF via the print dialogue. No PDF library is taken.
**Consequences:** Figures get out of the system today with no new risk. A direct PDF writer and an
Excel export remain available as a later convenience, and are recorded as such rather than as gaps.

### ADR-040 — Timestamps are stored in a form SQLite can order
**Status:** Accepted (Final QA)
**Context:** Final QA rendered the Razor components headlessly for the first time. Every screen in
the application failed on first render with
`SQLite does not support expressions of type 'DateTimeOffset' in ORDER BY clauses`. EF Core's own
SQLite mapping writes a `DateTimeOffset` as its *local* date and time followed by the offset, which
does not sort chronologically once two rows carry different offsets, so the provider refuses to
translate `ORDER BY`, `Min` and `Max` over such a column. The refusal is a runtime exception, and
twelve queries across the dashboard, payroll, loans, timesheets, reports and the audit trail hit
it. Nothing in the compiler, the Razor type check or the service-level tests could see it, because
those tests order in memory over lists they have already materialised.
**Decision:** `SortableDateTimeOffsetConverter` stores the instant first, in UTC, in a fixed-width
form, with the original offset appended: `2026-09-17T12:32:00.0000000Z+02:00`. Ordering the text is
then exactly ordering the instant, the offset still round-trips and no precision is lost. It is
applied in `OnModelCreating` to *every* `DateTimeOffset` property in the model rather than to a
chosen few, because a column the converter misses is a column the database cannot order by, and
that failure appears on a user's screen rather than in a build. The column type is unchanged —
SQLite stored these as TEXT already — so no migration is required; only the text inside changes,
and a database written by an earlier build is still read correctly by the fallback parse.
**Consequences:** Ordering by time works everywhere, in SQL, with the row limit applied by the
database rather than after loading the table. The regression guard is `Tawaka.Ui.Tests`, which
renders every route: if a future query reintroduces an unorderable column, a test fails rather than
a payroll officer's screen.

### ADR-041 — A business creates its own users
**Status:** Accepted (Final QA)
**Context:** Final QA's code review found that the Administration screen listed users read-only and
that nothing in the application — no service, no screen — could create one. An installation
therefore held exactly one account: the administrator generated at first run. Approval refuses
whoever calculated the run, timesheet approval refuses whoever captured it and loan approval
refuses whoever raised it, so a single-account installation can produce a development calculation
and nothing else: no approval, no finalisation, no statutory obligations, no payment. The control
model the whole product is built on was unreachable.
**Decision:** `UserAdministrationService` creates users, sets their roles, resets passwords,
disables and re-enables accounts and clears lockouts, all behind `Users.Manage`, and the
Administration screen exposes it. Every account is created with a generated password shown once
and `MustChangePassword` set, so the administrator creating an account never knows the password the
user ends up with. Segregation of duties is checked across the *union* of a user's roles, not only
within one role: two roles that are each permissible would otherwise combine into the set neither
is allowed to hold. A user is never deleted — their name is on payrolls they calculated and
approvals they gave — only disabled. The last active account that can manage users cannot be
disabled or have that permission taken away, and nobody can disable their own account.
**Consequences:** A business can set itself up: an officer, a manager, a viewer, each with their
own account. Editing the permissions *of a role* is still not exposed — `RoleService` remains
reachable only from tests — so the four seeded roles are what an installation has. That is recorded
as a known limitation rather than closed here, because the scope of this milestone is validation
and defect repair.
