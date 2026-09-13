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
