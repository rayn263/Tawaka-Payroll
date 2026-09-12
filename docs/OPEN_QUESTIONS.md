# Open Questions — decisions required

**Last updated:** 2026-09-12

`BLOCKING` items must be answered before the affected code is written. `NON-BLOCKING` items have a
sensible default recorded and can be changed later in Settings without rework.

---

## Compliance — must be resolved with a tax advisor or the authorities

| # | Question | Status | Default if unanswered |
|---|---|---|---|
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
