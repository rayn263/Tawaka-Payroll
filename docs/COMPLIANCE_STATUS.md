# Compliance status — Tawaka Payroll release candidate

**As at:** 17 September 2026
**Prepared during:** Final QA, Windows validation and release-candidate audit

This is the authoritative statement of what Tawaka Payroll can and cannot be trusted to do under
Zimbabwean payroll law as at the date above. It is written to be read by someone deciding whether
to run a real payroll on it. The short answer is: **not yet, and the software will not let you.**

---

## 1. The four statuses, and what each one means

These are kept strictly apart. They describe the **evidence**, never the implementation: a rule can
be implemented perfectly and still be `Unverified`, and nothing about the quality of the code moves
it.

| Status | Meaning | Usable in a live payroll? |
|---|---|---|
| **VERIFIED** | Confirmed against the official source, and that source is recorded on the rule. | Yes |
| **SUPPORTED** | Credible secondary sources agree, but the official document has not been read. | **No** |
| **UNVERIFIED** | Evidence insufficient, or sources conflict. | **No** |
| **UNRESOLVED** | Not a rule status at all. The *runtime* outcome when the engine has no usable rule for a figure: the figure is left absent and the reason named. Never a zero. | Not applicable |

**No rule in this release is VERIFIED.** Every seeded rule is `Unverified` and records where its
value came from. A rule is promoted only when a person reads the official document and records it —
never because the application implements the rule, and never because the figure looks right.

---

## 2. Access to authoritative sources

Every attempt to reach an authoritative Zimbabwean source from this environment has failed at the
network layer, and has done so consistently:

```
CONNECT www.zimra.co.zw:443  → 403   (gateway policy denial)
CONNECT www.nssa.org.zw:443  → 403
CONNECT www.veritaszim.net:443 → 403
```

Rechecked on 12 September, twice on 15 September, and again on 17 September during this audit. The
proxy reports `connect_rejected: gateway answered 403 to CONNECT (policy denial or upstream
failure)`. This is a property of the environment, not of the sites.

Consequence: **no rule in this release could have been verified from here, by anyone.** Verification
is work for a person with an internet connection and, for several of the questions below, with a
letter from NSSA or ZIMRA. Nothing has been substituted in the meantime.

---

## 3. The nine open questions

Each is stated in five parts, as required: what the application does, what evidence exists, what is
unknown, whether it blocks live payroll, and what would resolve it.

---

### Q1 — Multi-currency PAYE methodology

**What the application does.** Dual-currency treatment is a *rule*, not code: a
`CurrencyTaxStrategyRule` decides whether an employee paid in more than one currency is taxed once
on an aggregated amount or separately per currency, and which exchange rate applies. No such rule is
seeded, so the strategy resolves to nothing and the engine emits
`CURRENCY_STRATEGY_UNRESOLVED` against compliance question Q1. A single-currency employee is
unaffected — the strategy is only consulted when more than one currency is in play. Original
amounts and currencies are always preserved alongside any converted figure, with the rate, its date
and its source; a conversion never destroys what it converted.

**What evidence exists.** Secondary sources conflict directly. One position aggregates ZiG into USD
at an official rate and taxes once; the other taxes each currency on its own table. The difference
is material — it changes which band an employee falls in — so neither has been adopted.

**What is unknown.** Which position ZIMRA actually requires; which rate (interbank, official, RBZ
mid-rate) applies; and as at which date (pay date, period end, or date of payment).

**Does it block live payroll?** **Yes, for any employee paid in more than one currency.** A
single-currency payroll is not blocked by Q1.

**What would resolve it.** A ZIMRA public notice or Practice Note stating the treatment of
multi-currency remuneration for PAYE, naming the rate source and the rate date. A written ruling to
this employer would also do.

---

### Q6 — APWCS assessed rate and industry classification

**What the application does.** Refuses to compute it. No APWCS rule is seeded, because the rate is
assessed per employer by NSSA and no seeder is in a position to know it. A payroll run for an
employer with no APWCS rule produces `APWCS_RULE_UNRESOLVED` naming Q6, and the employer-cost
figure is absent rather than zero. APWCS is employer-borne throughout: it is never turned into an
employee deduction, and the obligation carries `IsDeductionApplicable = false`.

**What evidence exists.** That the scheme exists and is employer-borne. Nothing about *this*
employer's rate.

**What is unknown.** This company's industry classification, industrial code, assessed rate, and
the earnings the premium is computed on.

**Does it block live payroll?** **Yes** — employer cost cannot be stated without it. Employee net
pay is not affected, because APWCS is not deducted from anyone.

**What would resolve it.** NSSA Form WC50 registration and the assessment notice NSSA issues in
response, which states the classification and the rate. This is external: only NSSA can supply it.

---

### Q22 — The monthly NSSA ceiling on weekly and fortnightly payroll

**What the application does.** Treats the apportionment method as a field on the NSSA rule
(`CeilingApplication`), seeded **undetermined**. Where it is undetermined and the period is not
monthly, the engine emits `NSSA_CEILING_APPLICATION_UNRESOLVED` naming Q22 rather than choosing a
method. A monthly payroll is unaffected.

**What evidence exists.** That a monthly insurable-earnings ceiling exists. Three plausible
apportionments are in circulation — pro rata by period length, the full monthly ceiling applied to
each period, and a cumulative month-to-date ceiling — and they differ by roughly fourfold for a
weekly-paid employee.

**What is unknown.** Which of the three NSSA requires.

**Does it block live payroll?** **Yes, for weekly and fortnightly payroll.** Monthly payroll is not
blocked by Q22.

**What would resolve it.** The NSSA contribution schedule or employer guide stating how the ceiling
applies to non-monthly pay periods, or a written NSSA ruling.

---

### Q26 — The PAYE fixed-deduction ("less") column

**What the application does.** The PAYE table engine supports a per-band fixed deduction, and the
seeded tables **have no such column at all** — not a zero, an absence. Where a resolved table is
marked as requiring one and does not carry it, the engine emits
`PAYE_FIXED_DEDUCTION_UNRESOLVED`. The seeded tables are `Unverified` in any case, so PAYE cannot be
used live regardless.

**What evidence exists.** That ZIMRA's published tables have historically carried a "less" column
alongside the rate. The values for 2026, per band, per currency, per period basis, have not been
read from the official table.

**What is unknown.** Whether the 2026 tables carry the column, and if so every value in it.

**Does it block live payroll?** **Yes, universally.** This is the critical one: without it no PAYE
figure can be verified for any employee in any currency.

**What would resolve it.** The ZIMRA PAYE tables for tax year 2026 — the official published tables,
not a summary — for USD and ZWG, at each period basis in use.

---

### Q29 — Whether a ZWG *monthly* PAYE table exists

**What the application does.** Resolves a PAYE table per (currency, period basis, date). Where no
table resolves for the pair, the figure is left unresolved with `PAYE_TABLE_UNRESOLVED`. The seed
ships a ZWG **annual** table and no ZWG monthly table, so a monthly ZiG payroll produces an
unresolved PAYE figure — visibly, on the screen and the payslip, as a dash and never as `0.00`.

**What evidence exists.** Two claims are deliberately kept apart, and only the first is asserted:

- **(a) This environment cannot retrieve an authoritative ZWG monthly table.** True, and evidenced
  by the 403 responses above.
- **(b) No ZWG monthly table exists.** **Not claimed, and not encoded anywhere.** The only thing
  pointing that way is a third-party search listing that did not show one, and a listing that omits
  a document is not a document saying the thing is absent.

**What is unknown.** Whether ZIMRA publishes a ZWG monthly table, or whether monthly ZiG
remuneration is taxed on the annual table by an annual-equivalent method.

**Does it block live payroll?** **Yes, for ZiG payroll on any non-annual period.** The behaviour the
application implements — refuse to produce a figure — is correct whichever of (a) or (b) turns out
to hold.

**What would resolve it.** The ZIMRA PAYE table publication for 2026 showing which period bases are
published for ZWG; or a ZIMRA Practice Note describing the method for monthly ZiG remuneration.

---

### Q30 — Whether the 2026 tables are the 2025 tables carried forward

**What the application does.** Every tax rule is dated (`EffectiveFrom`/`EffectiveTo`) and resolved
as at the pay date. A rule whose effective period does not cover the pay date is not used — it is
not silently extended. If the 2026 figures are in fact the 2025 figures, that is a fact about the
data to be recorded when the tables are read, not an assumption for the software to make.

**What evidence exists.** That tables were published for 2025. No 2026 publication has been read.

**What is unknown.** Whether a separate 2026 publication exists, and if not, whether the 2025
tables remain in force for 2026 by operation of law or by a ZIMRA notice.

**Does it block live payroll?** **Yes, indirectly.** The seeded 2026 figures cannot be promoted to
`Verified` without seeing what they are meant to be verified against.

**What would resolve it.** The ZIMRA tax tables page for 2026, or a public notice stating that the
2025 tables continue to apply.

---

### Q31 — Overtime multipliers: contractual or statutory

**What the application does.** Overtime is priced by a dated `OvertimeRule` per category — ordinary
day, Sunday, public holiday. No multiplier is compiled into the engine. Rules are seeded for the
three categories at the commonly cited 1.5 and 2.0, marked `Unverified`, with a note on the rule
saying in terms that the figure is the commonly cited rate and not a verified one. Because they are
`Unverified` they resolve in **Development** and are refused in **Live**: a live run emits
`OVERTIME_RULE_UNRESOLVED` naming Q31 and leaves the overtime amount absent, not zero. The hours are
not lost either way — they remain on the approved timesheet and in the sealed snapshot.

**What evidence exists.** The Labour Act governs overtime; sectoral instruments and NEC collective
bargaining agreements set multipliers for particular industries. The commonly quoted 1.5 for
ordinary overtime and 2.0 for a rest day or public holiday appear in secondary summaries. They are
seeded as *configurable defaults*, marked `Unverified`, and are not claimed to be law.

**What is unknown.** Which multipliers are mandated by statute for this employer's sector, which
come from the applicable NEC CBA, and which are purely contractual.

**Does it block live payroll?** **Yes, for any employee with overtime.** An employee with no
overtime in the period is unaffected.

**What would resolve it.** The Labour Act provisions on overtime, plus the Statutory Instrument or
registered CBA for the construction sector (NECCIZ) — SI 112 of 2021 and any later agreement.

---

### Q32 — Statutory leave entitlements

**What the application does.** Leave types, entitlement days and pay treatment are configuration
rows, not code. Nothing is assumed to be a statutory entitlement: each seeded leave type is created
with `EntitlementDays = null`, `StatutoryEntitlementDays = null` and
`EntitlementVerificationStatus = Unverified` — an absence, not a guessed number that an employer
might take for the law. Unpaid leave reduces pay through a `PayDivisor` rule (see Q33), so if the
divisor is unresolved the deduction is unresolved too, rather than being computed on a guessed
divisor. A leave balance never silently goes negative: the balance is
`entitlement + accrued − taken − forfeited`, and each component is recorded.

**What evidence exists.** That the Labour Act provides for annual leave, sick leave on a full-pay
then half-pay scale, maternity leave and compassionate leave. The precise days, qualifying service
and pay treatment have not been read from the Act.

**What is unknown.** Days per year for each type, the qualifying conditions, the sick-leave
full/half-pay scale and its reset period, maternity qualification and pay treatment, and how
accrual works for part-time and casual employees.

**Does it block live payroll?** **Not for gross-to-net calculation**, provided no unpaid leave falls
in the period and the entitlements configured are the employer's own contractual ones. It **does**
block any claim that the leave module implements Zimbabwean statutory leave, and it blocks unpaid
leave deductions through Q33.

**What would resolve it.** The Labour Act [Chapter 28:01] provisions on leave, and the applicable
sectoral instrument where it improves on them.

---

### Q33 — The pay divisor

**What the application does.** Converting a monthly salary to a daily or hourly rate is a dated
`PayDivisorRule`, not a constant. Nothing in the engine divides by 22, or by 26, or by 30. One rule
is seeded — 22 working days and 176 ordinary hours — marked `Unverified` and noted on the rule as a
common convention rather than an established one. It therefore resolves in **Development** and is
refused in **Live**: in a live run any figure needing a divisor — an unpaid-leave deduction, a daily
rate derived from a monthly salary, an hourly overtime rate for a salaried employee — is unresolved
and names Q33.

**What evidence exists.** Several divisors are in common use in Zimbabwe (22 working days, 26 days,
30 days, and 'actual working days in the month'). They produce materially different deductions for
the same absence.

**What is unknown.** Which divisor is required, or permitted, for a statutory deduction as opposed
to a contractual one; and whether it differs by sector.

**Does it block live payroll?** **Yes, for any employee with unpaid leave, overtime on a salary, or
a mid-period start or end.** A full-month salaried employee with no absence is unaffected.

**What would resolve it.** The Labour Act or the applicable NEC CBA stating the basis for
converting a monthly wage to a daily or hourly rate, or a ZIMRA/Ministry of Labour statement on the
point.

---

## 4. What this means for the release

The release-stage mechanism computes the stage from the database and refuses to be talked out of it:

| Stage | Condition |
|---|---|
| **Development** | Fresh installation, no company configured |
| **Compliance unverified** | Configured, but one or more required rules are not `Verified` — **where this release candidate sits** |
| **Ready for controlled testing** | Every required rule `Verified`, company configured, parallel running not yet complete |
| **Live payroll enabled** | A person has recorded the decision, with a reason, after controlled testing |

`ReleaseReadinessService.SetLivePayrollAsync` **refuses** while any required rule is unverified, and
requires a recorded reason when it does succeed. That refusal is tested. The stage is shown in the
top bar of every screen and explained in full on Statutory → Compliance status.

**This release candidate is at "Compliance unverified" and cannot be moved past it by any action
available in the user interface.** That is the correct state, and it is the state the evidence
supports.

---

## 5. What a payroll actually produces today

- A **Development** calculation runs end to end and shows every figure it can compute.
- Every figure it cannot compute is **absent and named**, with the compliance question beside it.
- A payslip produced in this state carries a **DEVELOPMENT — NOT FOR STATUTORY USE** watermark.
- A run in Development mode **cannot be approved**, so it cannot be finalised, cannot create
  statutory obligations, and cannot be paid.

Nothing in the software will produce a figure it cannot account for, and nothing will tell an
employer that a tax has been paid when it has not.
