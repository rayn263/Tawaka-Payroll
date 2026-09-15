# Testing

**Last updated:** 2026-09-15

## Running the tests

```bash
./test.sh                 # every test in the cross-platform solution
./build.sh                # build only
./foundation-check.sh     # end-to-end foundation check (see below)
```

Requires the .NET 8 SDK. On Ubuntu: `apt-get install -y dotnet-sdk-8.0`.

Current result: **570 passing, 1 skipped, 0 failing, 0 warnings.**

> **A correction to the Milestone 5 figure of 608.** Five test classes inherited
> `StatutoryObligationTests`, so xUnit re-ran its 18 tests inside each of them — roughly 90 of
> that 608 were the same tests executed repeatedly. The shared fixture is now a fact-free
> `PayrollFixtureBase`, so every number reported here is a distinct test. The honest comparison is
> 518 distinct tests at Milestone 5 against 570 now.

## The two kinds of test

The distinction matters, because statutory rates are not yet verified and will change.

### Behaviour tests — independent of any statutory rate

These assert how the system behaves and stay valid when verified 2026 rules replace the seed data.
They are the majority of the suite and the ones that protect the design.

| Area | What is asserted |
|---|---|
| `Money` | USD and ZiG never combine without an explicit conversion; rounding is away from zero; ceilings and floors work within one currency |
| `CurrencyConversion` | Original amount, currency, rate, rate date, rate source and purpose are all retained; changing a stored rate does not alter a completed conversion |
| `StatutoryRuleResolver` | Verified rules resolve in live mode; Supported and Unverified do not; **no substitution** — it never falls back to another period, currency or grade; overlapping rules are reported rather than chosen between; a weekly table is never derived from the monthly one |
| `LivePayrollGate` | Every blocking rule is listed, not just the first; the message names the exact rules and the remedy |
| `CalculationTrace` | Rule, inputs, steps, raw value, rounding, output and conversion provenance are all captured |
| `AuditInterceptor` | Creates, updates and deletes are recorded with user, timestamp, field, old value and new value; unchanged fields are not logged; the audit trail does not audit itself |
| `PeriodLockInterceptor` | A locked period cannot be modified or deleted **at the data layer**; the record is unchanged after a rejected write; an authorised reopen requires a reason and works only inside its scope; a locked run's result rows and earning lines are refused too, while an unlocked run's figures stay editable (ADR-030) |
| Migration and seeding | The documented tables are created; seeding is idempotent; decimals round-trip exactly through scaled-integer storage |
| `PasswordHasher` | The hash never contains the password; the same password hashes differently each time; malformed stored hashes fail closed; weak iteration counts are refused and flagged for upgrade |
| `AuthenticationService` | Correct credentials return the user's roles and permissions; an unknown user and a wrong password give the **same** message, so the form cannot enumerate accounts; repeated failures lock the account; every attempt is recorded |
| Segregation of duties | `Payroll.Calculate` and `Payroll.Approve` cannot be held by one role; the shipped Payroll Officer cannot approve and the shipped Manager cannot calculate; disabling the check is deliberate and audited |
| `EmployeeService` | Duplicate employee numbers and national IDs are refused; leaving is a status change, never a delete; every status change is recorded; permissions are enforced |
| `EmployeeContractService` | **A salary change creates a new version and preserves the old one**; the contract applying on a past date is still resolvable; only one contract is ever current; superseding requires a reason and cannot be backdated before the version it replaces |
| Employee validation | Required fields, negative salary, end-before-start dates, missing currency, missing rate for the earnings basis, bank versus mobile-money requirements, percentage allocation bounds |
| `PayrollCalculator` | The full pipeline against the seed rules: TC-01 to TC-15, TC-19, TC-21 to TC-23 |
| Unresolved behaviour | A missing or unverified rule produces an **absent** figure with its compliance question, never 0.00; weekly payroll refuses an undetermined ceiling application; monthly payroll is unaffected by it; a published-form table without its fixed-deduction column refuses; mixed-currency earnings refuse without an approved strategy |
| Financial invariants | Across 19 salaries including every band boundary: gross less deductions equals net; totals reconcile to lines; statutory deductions never exceed gross; net pay is never negative; insurable earnings never exceed the ceiling; PAYE and net pay both rise monotonically with income; the levy is always 3% of tax after credits; employer contributions never reduce net pay; USD and ZiG cannot be added |
| `StatutoryObligation` | The four states move independently; approving a payroll run never marks an obligation paid; an obligation cannot be approved before it is calculated and deducted; a payment cannot be recorded before approval; a payment without a reference, in the wrong currency, or exceeding the outstanding balance is refused; a partial payment leaves the correct balance; `IsPaid` is derived from payments and has no setter; a reversal needs a reason and retains the row; employer-borne obligations report deduction as not applicable; each obligation reconciles to its per-employee lines |
| `PayslipBuilder` | The payslip renders persisted rows only; its totals reconcile to the stored run-employee totals; a legitimate zero renders `0.00` and an unresolved figure renders `—`; configured statutory categories appear even at zero; a USD and a ZiG payslip each carry their own currency throughout; a development-mode copy is watermarked and cannot be issued; re-issuing after a correction creates a new revision and supersedes the previous one without destroying it |
| `PayrollReportService` | Every report groups by currency and no total spans currencies; the summary reconciles to the register; gross plus employer contributions equals total employer cost; the statutory report reconciles to the obligation register; project allocations sum to the total |
| Workflow | Review → Approve → Finalise → Paid → Locked, each permissioned; the calculator cannot approve; a run cannot be finalised before approval; a correction run leaves the original run's figures and obligations untouched; a statutory payment is still possible after the run is locked |
| `TimesheetService` | A timesheet moves Draft → Submitted → Approved; the submitter cannot approve it; a day outside the period, a duplicate date, more than 24 hours on one day and a second live timesheet for the same period are all refused; the first and last day of the period are accepted; an approved timesheet is not editable; returning one needs a reason and makes it editable again; a correction supersedes the original without touching it |
| `LeaveService` | An unestablished entitlement leaves the balance undetermined, never zero; overlapping leave is refused and abutting leave is not; a rejected request does not block a later one; the submitter cannot approve; withdrawing an approval reverses the days and keeps both rows; adjustments are ledger rows with a reason; reclassifying a leave type does not reprice leave already taken |
| `HolidayCalendarService` | No holiday is seeded; a captured public holiday starts Unverified and a company one does not; verifying needs a source; a duplicate date and a date outside the calendar's period are refused; removing deactivates rather than deletes |
| `LoanService` | Instalments sum exactly to what is repayable, with rounding absorbed by the last one; the balance reconciles to the ledger after every movement; a deduction is capped at the outstanding balance; over-recovery needs a named approval with a reason; early settlement cancels the remaining schedule; a reversal restores the balance, keeps both rows and cannot be applied twice; the raiser cannot approve |
| `TimeAndAbsenceCalculator` | Approved hours drive an hourly employee's basic pay; no approved time leaves it unresolved rather than zero; each overtime category is priced by its own rule and categories are never merged; a category with no rule refuses instead of paying plain time; overtime for a salaried employee without a divisor rule refuses with Q33; unpaid leave is docked at the daily rate and paid leave produces no line |
| `PayrollSnapshotStore` | The snapshot is stored, hashed and sealed at calculation; it round-trips with its figures and currency intact; a tampered row is refused; serialisation is deterministic; approved inputs are queryable in both directions; a later loan repayment or timesheet correction does not change what a completed run recorded |
| End-to-end | The whole journey in one test, for USD and for ZiG independently: company, employee, contract, earnings, project and site, time, overtime, leave, loan, approval by a second person, run, snapshot, calculate, explain, review, approve, finalise, obligations, net wages paid, statutory payment, payslip, reports, lock, and four separate attempts to modify a locked run — each refused |
| Reconciliation | Payslip ↔ result, reports ↔ result, obligations ↔ result and ↔ their own lines, employer cost = gross + contributions, project allocations = total cost, deductions = total deductions, gross − deductions = net, journal balances and reconciles, and the stored snapshot still reproduces the figures it produced |
| Integrity | Foreign keys enforced by SQLite itself; every company-scoped table carries a company; national rules are company-neutral; no currency column holds a blank |
| Release readiness | A fresh installation reports DEVELOPMENT; configured-but-unverified reports COMPLIANCE-UNVERIFIED; verified and configured reaches READY FOR CONTROLLED TESTING and no further; live payroll is refused while any rule is unverified, whatever reason is given |
| Backup and restore | A backup writes a manifest and restores cleanly; restoring without confirming is refused; a folder that is not a backup is refused; a backup whose database was altered is refused |
| Demonstration data | It covers every employment type, both currencies and every earnings basis; it stamps the installation; it **refuses** to seed a database that already holds employees; its period is development mode; it seeds no public holiday it cannot evidence |
| `PayrollRunService` | Historical reproducibility — a later salary increase and a future tax table both leave a calculated September untouched; an approved run cannot be silently recalculated; a locked run is immutable; a development run cannot be approved; a run with unresolved figures cannot be approved; the calculator cannot approve their own run |

### Statutory seed tests — against clearly labelled temporary rules

These use the seed rules from the compliance specification, which are **not verified**. Expected
values live in `SeedExpectations` in
`tests/Tawaka.Payroll.Engine.Tests/ComplianceTestCatalogue.cs` as data rather than as constants
buried in assertions, so that when verified 2026 tables are loaded the table is regenerated from
the authoritative rule dataset and the tests that consume it update with it.

Two meta-tests guard the recorded expectations today:

- `Seed_expectations_are_internally_consistent` — gross less deductions equals the stated net pay
  for every case.
- `Seed_expectation_confirms_aids_levy_is_charged_after_credits` — TC-21's levy is exactly 3% of
  post-credit tax, and is lower than TC-01's. This is what makes the Q2 answer a test rather than
  a note.

Seeding itself is also asserted honestly:

- `No_seeded_rule_is_marked_verified` — if this ever fails, either a rule was genuinely verified
  (and the specification should say so) or the gate has been undermined.
- `Every_seeded_rule_records_where_its_value_came_from`
- `Seeded_paye_tables_have_no_fixed_deduction_column` — records the Q26 gap explicitly, so it
  cannot be quietly assumed to be zero.
- `Nssa_seed_leaves_the_ceiling_application_undetermined` (Q22)
- `Conflicting_medical_credit_is_seeded_inactive_with_no_percentage` (Q24)
- `Sdf_levy_is_seeded_inactive_because_liability_is_unestablished`
- `Every_earning_and_deduction_type_records_its_treatment_grade_and_source` — the statutory
  treatment of an allowance is graded and sourced exactly like a tax table (ADR-021)
- `Overtime_is_taxable_but_excluded_from_nssa` — the seeded treatment matching §14 of the spec
- `Every_employment_type_has_a_matching_nssa_eligibility_rule` — coverage can never fall back to
  an assumption

## Invariant tests

`InvariantTests` asserts properties that must hold for every calculation, across a spread of
salaries chosen to sit on band boundaries and rounding edges. These caught a real defect during
Milestone 3: unrounded input amounts made payslip totals disagree with the lines that composed
them. Earnings are now rounded to currency precision on entry.

## The 34 compliance cases

`ComplianceTestCatalogue.cs` holds all 34 cases from
`ZIMBABWE_PAYROLL_COMPLIANCE_SPEC_V1.md` §24 in one place, so the whole compliance surface appears
in every test run.

**Implemented (33 of 34).** Milestone 3 activated most of the catalogue against the real engine;
Milestone 4 activated TC-28, TC-29 and TC-29b against the statutory obligation register; Milestone 5
activated TC-20 (advance recovered post-tax) and TC-33 (the casual six-week engagement warning) now
that loans and timesheets exist. `ComplianceTestCatalogue.cs` lists where each case now lives.

**Still skipped (1):** TC-18, part-time NSSA ceiling treatment, which depends on compliance
questions Q4a and Q22. It stays in the file so the outstanding work is counted every time the suite
runs.

## Running a payroll by hand

```bash
./foundation-check.sh                                  # rule register and the live gate
dotnet run --project tools/Tawaka.Foundation.Cli -- --payroll
dotnet run --project tools/Tawaka.Foundation.Cli -- --payroll --verify-rules
```

`--payroll` creates two employees — one on USD 850, one on ZiG 15,000 — a September 2026 period and
a set of payroll inputs: a full month's timesheet with four hours of overtime, two days of unpaid
leave and a USD 600 loan over six instalments. The officer captures all three, the manager approves
them and the administrator disburses the loan, so segregation of duties is exercised across three
identities rather than described. It then prints the approved inputs the run consumed, the preview,
one full calculation explanation and an approval attempt. It signs in as a payroll officer to calculate and as a manager to approve,
so segregation of duties is exercised rather than described. Without `--verify-rules` the approval
is refused and the run stops there, which is the live payroll gate doing its job: a development
calculation can never produce a statutory liability that looks settled.

`--verify-rules` additionally marks the seeded rules verified and runs the period in LIVE mode, as
a real verification exercise would, so the workflow can be seen end to end. It then finalises the
run, prints the obligation register, approves USD PAYE, records a **part** payment against it and
attempts a second payment with no reference — which is refused. The register is printed after each
step so the four states can be watched moving independently, and the final balance is still
outstanding.

`--verify-rules` is a demonstration switch and says so loudly on every run: it does not make the
rules correct, no rule has been verified against an authoritative source, and the application never
does this by itself.

## Verifying the foundation by hand

`./foundation-check.sh` creates a fresh SQLite database from the real migrations, seeds the
statutory baseline, prints the rule register with verification status, and runs the live payroll
gate for a September 2026 monthly USD payroll. Expected output ends with:

```
Total 24 rules — Unverified: 14, Supported: 10

LIVE PAYROLL GATE — September 2026, monthly, USD
LIVE PAYROLL BLOCKED
One or more statutory rules required for this payroll have not been verified.
...
Result: payroll may run in DEVELOPMENT mode only.
```

That output is the foundation working as designed: the system knows exactly which rules it lacks
and refuses to produce a live payroll until they are verified.

## What is not tested

- **Screen behaviour.** Every Razor component now compiles and is type-checked on each build
  (ADR-019), which catches a large class of mistakes, but no screen has been rendered or clicked.
  Data binding, navigation and form round-trips are unverified. Component tests using bUnit are
  proposed for Milestone 5.
- **The WPF host.** It targets `net8.0-windows` and has never been compiled here. It is now thin —
  a window, a WebView and startup wiring — so the untested surface is small.
- **Printing and PDF.** The payslip and the reports print through the browser's print dialogue
  against a print stylesheet. The stylesheet is not exercised by any test; it needs a look on
  Windows.
- **Bank files and statutory submission files.** Not built, so not tested — the formats could not
  be obtained from an authoritative source.
