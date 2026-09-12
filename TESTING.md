# Testing

**Last updated:** 2026-09-12

## Running the tests

```bash
./test.sh                 # every test in the cross-platform solution
./build.sh                # build only
./foundation-check.sh     # end-to-end foundation check (see below)
```

Requires the .NET 8 SDK. On Ubuntu: `apt-get install -y dotnet-sdk-8.0`.

Current result: **91 passing, 31 skipped, 0 failing.**

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

## The 34 compliance cases

`ComplianceTestCatalogue.cs` holds all 34 cases from
`ZIMBABWE_PAYROLL_COMPLIANCE_SPEC_V1.md` §24 in one place, so the whole compliance surface appears
in every test run.

**Implemented now (4):** TC-26 historical rule versioning · TC-31 unverified rule blocks live
payroll · TC-32 the same rule calculates in development mode · TC-34 a missing period table blocks
rather than deriving. TC-30 (locked payroll modification) is implemented in
`Tawaka.Infrastructure.Tests`, where the interceptor it exercises lives.

**Skipped, with the reason stated (30):** cases needing the calculation engine (Milestone 3), the
employee and contract model (Milestone 2), or the statutory obligation register (Milestone 4).
They are skipped rather than omitted so the outstanding work is counted every time the suite runs.

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

- **The desktop shell.** It targets `net8.0-windows` and has never been compiled; this environment
  is Linux. Treat it as untested code until it builds on Windows.
- **Payroll calculation.** None exists yet.
