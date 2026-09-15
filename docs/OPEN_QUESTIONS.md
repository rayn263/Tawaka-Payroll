# Open Questions — decisions required

**Last updated:** 2026-09-12 (revised after compliance research — see `ZIMBABWE_PAYROLL_COMPLIANCE_SPEC_V1.md`)

`BLOCKING` items must be answered before the affected code is written. `NON-BLOCKING` items have a
sensible default recorded and can be changed later in Settings without rework.

---

## Compliance — must be resolved with a tax advisor or the authorities

| # | Question | Status | Default if unanswered |
|---|---|---|---|
> **Status update.** Q1–Q6 were researched in `ZIMBABWE_PAYROLL_COMPLIANCE_SPEC_V1.md`. Q2 and Q3
> are resolved in substance, Q5 largely so, Q4 partly. Q1 advanced but remains open. Q6 is
> externally dependent on NSSA. Eight new blocking questions (Q21–Q28) were discovered and are
> listed at the end of this file. The authoritative status register is §25 of the spec.

| **Q1** | **Multi-currency PAYE:** for an employee paid in both USD and ZiG, is ZiG converted and aggregated into the USD table (position A), or is each currency taxed on its own table (position B)? Sources conflict, and the difference is material. Which exchange rate (interbank/official/RBZ) and as at which date? | **BLOCKING** for mixed-currency payroll | Aggregate in USD, tax once, apportion pro rata for remittance; flagged `Unverified`, blocked from live use |
| **Q2** | **AIDS Levy base:** 3% of tax *before* or *after* tax credits? | BLOCKING for credit-entitled employees | After credits; configurable |
| **Q3** | **Weekly/fortnightly PAYE:** does ZIMRA publish weekly/fortnightly tables, or is an annual-equivalent method used? How is the NSSA ceiling apportioned for a weekly-paid employee? | **BLOCKING** for weekly payroll | Per-period table where available; ceiling pro-rated by period length |
| **Q4** | **NSSA insurable earnings:** basic salary only, or basic plus specified allowances? Which allowances? | **BLOCKING** | Configurable per earning type via `IsNssaApplicable`; seeded to basic only |
| **Q5** | **NSSA eligibility:** are casual, occasional, seasonal, part-time and intern employees covered? Any age bounds? | BLOCKING for those types | Eligible unless configured otherwise; warning raised until confirmed |
| **Q6** | **APWCS:** what is this company's industry classification, industrial code and assessed rate, and what earnings is the premium computed on? Obtain from NSSA. | BLOCKING for employer cost accuracy | None — the system refuses to compute APWCS until a rate is entered |
| **Q7** | Current verified PAYE tables (USD and ZiG) for tax year 2026, with the official "less" / fixed-deduction column if used | **BLOCKING** | Seed values in `COMPLIANCE_ZIMBABWE.md` §2, all `Unverified` |
| **Q8** | Current tax credit amounts and conditions; current bonus/retrenchment exemption limits per currency | NON-BLOCKING | Credits seeded per §2.6 `Unverified`; exemptions seeded empty rather than guessed |
| **Q9** | Is the company liable for ZIMDEF (1%) and the Standards Development Fund (0.5%)? Is it a member of a NEC, and what are that NEC's employee and employer rates? | NON-BLOCKING | Levies created but inactive until enabled |
| **Q10** | Does the company have a registered pension scheme, medical aid scheme or funeral scheme with payroll deductions? Pre- or post-tax? | NON-BLOCKING | Deduction types created, inactive |

## Business and operational

| # | Question | Status | Default |
|---|---|---|---|
| **Q11** | Which payroll frequencies are actually used, and on what cycle (e.g. weekly Friday for site staff, monthly for office)? | NON-BLOCKING | Monthly + weekly enabled |
| **Q12** | How many employees now, and realistically in three years? (Decides SQLite vs. a server database and whether concurrent users are needed.) | NON-BLOCKING | SQLite, single user at a time |
| **Q13** | How many people will use the system, and who approves payroll? Should segregation of duties (calculator ≠ approver) be enforced? | NON-BLOCKING | Enforced by default |
| **Q14** | Is there existing employee/payroll data to import (Excel, another system), and does year-to-date opening data need to be loaded mid-year? | NON-BLOCKING | Manual capture; a YTD opening-balance import is added if needed |
| **Q15** | Overtime rules actually applied: normal, Sunday, public holiday and night shift multipliers, and their legal or NEC basis | NON-BLOCKING | 1.5 normal / 2.0 Sunday and public holiday; configurable |
| **Q16** | Standard working hours per day and days per week, per employment type | NON-BLOCKING | 8 hours, 5 days |
| **Q17** | Company logo, letterhead, payslip footer wording, signature block and payslip numbering format | NON-BLOCKING | `PS-{year}-{period}-{sequence}` |
| **Q18** | Which accounting system will payroll eventually feed, and does it have a required import format? | NON-BLOCKING (Phase 3) | Generic balanced CSV journal |
| **Q19** | Will payslips be emailed, and to which employee addresses? (Has data protection implications.) | NON-BLOCKING (Phase 3) | Print/PDF only |
| **Q20** | How long must payroll records be retained, and where should backups live? | NON-BLOCKING | Retained indefinitely; local scheduled backup |

## Answers log

Record answers here as they are given, with the date and who gave them, then update the affected
rule rows and set their `VerificationStatus`.

| # | Answer | Given by | Date |
|---|---|---|---|
| | | | |


---

## New blocking questions discovered during compliance research (Q21–Q33)

Full detail and reasoning in `ZIMBABWE_PAYROLL_COMPLIANCE_SPEC_V1.md` §25.2.
Q29 and Q30 were found during the Milestone 4 access recheck (§0.1a and §1.4a).
Q31–Q33 were found during Milestone 5, building time, leave and loan inputs.

| # | Question | Impact | Status |
|---|---|---|---|
| **Q21** | Is Final Deduction System cumulative reconciliation mandatory for this employer, and how are mid-year joiners/leavers handled? | HIGH | OPEN |
| **Q22** | How is the monthly NSSA ceiling applied to weekly and fortnightly payroll? Three plausible methods differ roughly fourfold. | HIGH | OPEN |
| **Q23** | Construction NEC (NECCIZ) levy rates, base, employer/employee split and minimum wage grades — SI 112 of 2021 and later CBAs | HIGH (if construction) | OPEN |
| **Q24** | Medical aid credit: 50% or 100% of contributions? Two professional sources conflict directly. | MEDIUM | OPEN |
| **Q25** | Monthly PAYE return — P2 or REV5 on TaRMS? | MEDIUM | OPEN |
| **Q26** | **PAYE fixed-deduction ("less") column values** for every band, both currencies, every period basis | **CRITICAL** | OPEN |
| **Q27** | Employer loan benefit benchmark — SOFR or LIBOR? | LOW | OPEN |
| **Q28** | Does the bonus exemption apply to any bonus, or only an annual/13th-cheque bonus? | MEDIUM | OPEN |
| **Q29** | Does ZIMRA publish a **ZWG monthly** PAYE table, or is monthly ZiG remuneration taxed on the annual table? | HIGH | OPEN |
| **Q30** | Are the 2026 PAYE tables actually the 2025 tables carried forward, with no separate 2026 publication? | MEDIUM | OPEN |
| **Q31** | Overtime multipliers: which are contractual and which are mandated by the Labour Act or the applicable NEC CBA? | HIGH | OPEN |
| **Q32** | Statutory leave entitlements: annual, sick (full/half pay scale), maternity, compassionate — days, qualification and pay treatment | HIGH | OPEN |
| **Q33** | Pay divisor: working days and ordinary hours per month, for converting a salary to a daily and hourly rate | HIGH | OPEN |

---

## Still explicitly tracked as at Milestone 5 (2026-09-15)

As instructed, these remain open and are restated here so they cannot drift out of sight:

- **Q1** — dual-currency PAYE methodology. Advanced, not resolved.
- **Q6** — APWCS assessed rate and industry classification. Externally dependent: only NSSA can
  give it, via Form WC50.
- **Q22** — how the monthly NSSA ceiling applies to weekly and fortnightly payroll.
- **Q26** — the PAYE fixed-deduction ("less") column. Critical; blocks all PAYE verification.
- **Q29** — whether a ZWG *monthly* PAYE table exists. See the note below.
- **Q30** — whether the 2026 tables are the 2025 tables carried forward.

None of these moved during Milestone 5, and none was worked around in code. Each is represented in
the engine by a named `UnresolvedItem` rather than by a substituted figure.

### Q29 — two claims, kept apart

The instruction was to distinguish these, and they are recorded separately:

- **(a) This environment cannot retrieve an authoritative ZWG monthly PAYE table.** True, and
  evidenced: ZIMRA returned `403` at the CONNECT stage on 12 September and twice on 15 September.
- **(b) No ZWG monthly PAYE table exists.** **Not claimed.** The only thing pointing that way is a
  third-party search listing that did not show one, and a listing that omits a document is not a
  document saying the thing is absent.

Nothing in the codebase encodes (b). What it encodes is that a table which cannot be resolved for a
(currency, period basis) pair leaves the figure unresolved — which is the correct behaviour whether
(a) or (b) turns out to hold. Full note at `ZIMBABWE_PAYROLL_COMPLIANCE_SPEC_V1.md` §1.4a.
