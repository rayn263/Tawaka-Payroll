# Testing

**Last updated:** 2026-09-12

## Running the tests

```bash
./test.sh                 # every test in the cross-platform solution
./build.sh                # build only
./foundation-check.sh     # end-to-end foundation check (see below)
```

Requires the .NET 8 SDK. On Ubuntu: `apt-get install -y dotnet-sdk-8.0`.

Current result: **408 passing, 6 skipped, 0 failing.**

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
| `PeriodLockInterceptor` | A locked period cannot be modified or deleted **at the data layer**; the record is unchanged after a rejected write; an authorised reopen requires a reason and works only inside its scope |
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

**Implemented (28 of 34).** Milestone 3 activated most of the catalogue against the real engine.
`ComplianceTestCatalogue.cs` lists where each case now lives.

**Still skipped (6):** TC-18 (part-time ceiling treatment, pending Q4a/Q22), TC-20 (advance
recovery) and TC-33 (casual six-week warning) need modules not yet built; TC-28, TC-29 and TC-29b
need the statutory obligation register in Milestone 4. They stay in the file so the outstanding
work is counted every time the suite runs.

## Running a payroll by hand

```bash
./foundation-check.sh                                  # rule register and the live gate
dotnet run --project tools/Tawaka.Foundation.Cli -- --payroll
dotnet run --project tools/Tawaka.Foundation.Cli -- --payroll --verify-rules
```

`--payroll` creates two employees — one on USD 850, one on ZiG 15,000 — a September 2026 period,
and calculates them through the engine, then prints the preview, one full calculation explanation
and an approval attempt. It signs in as a payroll officer to calculate and as a manager to approve,
so segregation of duties is exercised rather than described.

`--verify-rules` additionally marks the seeded rules verified, as a real verification exercise
would, so the engine can be seen calculating end to end. It is a demonstration switch: it does not
make the rules correct, and the application never does this by itself.

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
  proposed for Milestone 3.
- **The WPF host.** It targets `net8.0-windows` and has never been compiled here. It is now thin —
  a window, a WebView and startup wiring — so the untested surface is small.
- **Payroll calculation.** None exists yet.
