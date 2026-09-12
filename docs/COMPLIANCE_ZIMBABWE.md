# Zimbabwe Statutory Requirements — Research, Status and Risks

**Document status:** Research findings, awaiting verification and approval
**Research date:** 2026-09-12

---

## 0. Research limitation — read this first

**The official primary sources could not be reached from the environment in which this research
was carried out.** `zimra.co.zw`, `nssa.org.zw` and several Zimbabwean advisory sites are blocked
by this session's network egress policy. The findings below were assembled from reachable
secondary sources (payroll vendors, tax advisory firms, EOR providers and compliance guides)
via web search.

Consequences, which are structural rather than cosmetic:

1. **No figure in this document may be treated as verified.** Every rate, band, threshold and
   ceiling below is seed data pending confirmation.
2. The system's rule tables therefore carry `VerificationStatus`, `SourceReference`, `VerifiedBy`
   and `VerifiedAt` columns, and the dashboard warns when an `Unverified` rule is about to be used
   for a live payroll run.
3. Secondary sources **disagree with each other** on at least one material point (multi-currency
   PAYE methodology — see §3). That disagreement is preserved here as an open question rather than
   resolved by picking the more convenient answer.
4. Before go-live, someone with access must verify each row of §2 against the current ZIMRA public
   notice, NSSA gazette, Finance Act and applicable Statutory Instruments, and set the
   verification fields. This is tracked as Q1 in `OPEN_QUESTIONS.md`.

Confidence legend: **M** = multiple independent secondary sources agree · **S** = single or weak
secondary source · **C** = sources conflict · **U** = not established.

---

## 1. Statutory obligations identified

| # | Obligation | Authority | Borne by | Configurable rule table |
|---|---|---|---|---|
| 1 | PAYE (employees tax) | ZIMRA | Employee (withheld by employer) | `TaxRules` + `TaxBrackets` |
| 2 | AIDS Levy | ZIMRA | Employee | `AidsLevyRules` |
| 3 | Tax credits (elderly, disabled, blind, medical) | ZIMRA | Reduce employee tax | `TaxCreditRules` |
| 4 | Exemptions (e.g. annual bonus limit) | ZIMRA | Reduce taxable income | `TaxExemptionRules` |
| 5 | NSSA POBS — employee | NSSA | Employee | `NssaRules` |
| 6 | NSSA POBS — employer | NSSA | **Employer** | `NssaRules` |
| 7 | NSSA APWCS (workers compensation) | NSSA | **Employer only** | `ApwcsRules` |
| 8 | ZIMDEF manpower development levy | ZIMDEF | **Employer** | `EmployerLevyRules` |
| 9 | Standards Development Fund levy | SAZ/Ministry | **Employer** | `EmployerLevyRules` |
| 10 | NEC dues (industry-specific, e.g. construction) | Relevant NEC | Employee and/or employer | `EmployerLevyRules` + `DeductionTypes` |

Items 8–10 were not in the original specification but are real Zimbabwean employer payroll
obligations, so the schema accommodates them from the start.

---

## 2. Findings — seed values pending verification

### 2.1 PAYE — USD, monthly (tax year 2026)

| Band (monthly) | Rate |
|---|---|
| 0.01 – 100.00 | 0% |
| 100.01 – 300.00 | 20% |
| 300.01 – 3,000.00 | 25% |
| Above 3,000.00 | 40% |

Confidence: **M**. Tax-free threshold USD 100/month (USD 1,200/year).
Top marginal effective rate with the AIDS Levy quoted as 41.2%, which implies the levy is charged
on the tax figure (40% + 3% × 40% = 41.2%).

### 2.2 PAYE — ZWG/ZiG (tax year 2026)

| Band (annual) | Rate |
|---|---|
| 0 – 33,600 | 0% |
| 33,600 – 100,800 | 20% |
| 100,800 – 1,008,000 | 25% |
| Above 1,008,000 | 40% |

Confidence: **S**. One source quotes a ZiG tax-free threshold of 2,800/month, which is consistent
with the 33,600 annual figure. Separate USD and ZiG schedules were reportedly published by ZIMRA
in December 2024, with the earlier ZWL bands obsolete. **The bracket boundaries, and whether a
fixed-deduction ("less") column is used, must be confirmed against the official table.**

### 2.3 AIDS Levy

- Rate: **3%**. Confidence: **M**.
- Base: charged on the income tax calculated. Whether the base is tax **before** or **after** tax
  credits is not established from the sources consulted — see risk **R-04**. Modelled as the
  configurable `AidsLevyRules.Base`; seed default `TaxAfterCredits`, flagged `Unverified`.

### 2.4 NSSA — Pension and Other Benefits Scheme (POBS)

- Employee: **4.5%**; Employer: **4.5%**; total 9%. Confidence: **M**.
- Insurable earnings ceiling: **USD 700 per month** (maximum employee contribution USD 31.50).
  Confidence: **M**.
- One source states the ceiling is **gazetted quarterly** — meaning it changes several times a
  year. This is precisely why the ceiling is a dated rule row and not a constant, and why the
  system warns on expiring rules. Confidence: **S**.
- What counts as insurable earnings (basic only, or basic plus specified allowances) is **not**
  established. Modelled as `NssaRules.EarningsBasis`. Confidence: **U** — see risk **R-05**.
- Age bounds and the treatment of casual/temporary employees are **not** established. Modelled as
  `NssaRules.MinimumAge`/`MaximumAge` and `NssaEligibilityRules`. Confidence: **U**.
- Deadline: contributions and the **P4 monthly return** due by the **10th** of the following
  month, filed through the NSSA Self-Service Portal. Confidence: **M**.

### 2.5 NSSA — APWCS (Accident Prevention and Workers Compensation Scheme)

- **100% employer funded**; nothing is deducted from the employee. Confidence: **M**.
- Rate varies by **industry risk classification**; low-risk (retail, offices) pay less, high-risk
  (mining, construction) considerably more. Each industrial classification is assigned an
  insurance rate from a risk analysis. Confidence: **M**.
- The specific rate for any given industry code is **not** established, and the premium base
  (described in one source as "total basic earnings") is **not** confirmed. Confidence: **U** —
  see risk **R-06**. The company must obtain its assessed rate and classification directly from
  NSSA; the system stores it in `ApwcsRules` with an effective date.

### 2.6 Tax credits

- Elderly (age 55+) and disabled persons' credits quoted at **USD 75/month (USD 900/year)**.
  Confidence: **S**.
- Medical: **100% of medical aid contributions** and **50% of other qualifying medical expenses**
  allowed as a credit, including for approved family members. Confidence: **S**.
- Where the employee is paid in local currency, credit amounts are converted at the prevailing
  interbank rate on the date of payment. Confidence: **S**.
- Blind persons' credit exists; the disabled persons' credit is reportedly not available to
  non-residents. Confidence: **S**.

### 2.7 Other employer levies

| Levy | Rate | Base | Confidence |
|---|---|---|---|
| ZIMDEF manpower training levy | **1%** | Gross wage bill (leviable items). One source describes the base as inclusive of allowances, bonuses, benefits, employer NSSA, medical aid, NEC and pension contributions. Statutory basis cited: Manpower Planning & Development Act (Ch. 28:02) s.53 and SI 74 & 392 of 1999. | **M** (rate) / **S** (base) |
| Standards Development Fund | **0.5%** | Gross wage bill, due by the 10th of the following month. | **S** |
| NEC dues | Industry-specific | Set by the relevant National Employment Council (e.g. construction). Must be obtained from the company's NEC. | **U** |

### 2.8 Deadlines and returns

| Item | Deadline | Confidence |
|---|---|---|
| PAYE remittance to ZIMRA | 10th of the following month | **M** |
| P2 monthly PAYE return | Monthly, with remittance | **M** |
| NSSA contributions + P4 return | 10th of the following month | **M** |
| ITF16 annual return | Annually; **separate returns per currency** where employees were paid in more than one currency | **S** |
| SDF levy | 10th of the following month | **S** |

Penalties reported: PAYE late payment up to 100% of the tax due plus interest around 10% per
annum; late P2 filing a civil penalty of the order of USD 30 per day up to about 91 days; NSSA
penalties applied automatically by the portal on late P4 uploads. Confidence: **S** — used only
to drive warning messages and due-date reminders, never to compute a charge automatically.

### 2.9 Labour law rules that affect payroll

| Rule | Finding | Confidence |
|---|---|---|
| Casual work definition | Engagement for not more than a total of **six weeks in any four consecutive months** (Labour Act Ch. 28:01) | **M** |
| Casual → permanent | A casual employed for a continuous period of four months is deemed permanent (cited as s.12(3)) | **S** |
| Annual leave | 1 day per month worked; ~12 working days per year after a year of continuous service (some sources cite a higher figure for certain sectors) | **S** |
| Sick leave | Up to 90 days on full pay, plus a further 90 days on half pay, per year (s.14) | **S** |
| Maternity leave | Up to 98 days fully paid (s.18), generally subject to a qualifying service period | **S** |

Consequence for the system: the casual six-week rule is a **dated, automatic warning**, not a
silent reclassification. When a casual employee approaches six weeks in a rolling four-month
window, the dashboard raises it for a human decision. The system must never reclassify an
employee's employment type by itself — that is a legal determination with contractual
consequences.

---

## 3. Multi-currency PAYE — the central unresolved question

Secondary sources **directly contradict each other**:

| Position | As stated by sources |
|---|---|
| **A. Aggregate and convert** | "Where an employer pays employees in both the ZWG and the US$, the ZWG income should be converted to US$ and then added to the US$ income, then all the taxable income will be taxed using the US$ tax tables." |
| **B. Tax each currency separately** | "Each element is taxed in its own currency and then reported cumulatively on the monthly return (P2)… never convert one currency to the other for calculation purposes." |

These produce **materially different tax**, because position B effectively grants the tax-free
threshold and the lower brackets twice. For an employee on USD 600 plus a ZiG amount, the
difference is not marginal.

Both sources agree on one operational point (confidence **M**): **USD liabilities are remitted to
ZIMRA's foreign currency account and ZiG liabilities to the domestic taxes account**, and separate
ITF16 returns are filed per currency. Any methodology must therefore end with tax split by
currency for remittance, even if it was computed on an aggregate.

**How the system handles this:** it does not choose. `CurrencyTaxStrategies` is a versioned,
dated, named rule with an `ApprovedByAdvisor` flag and an advisor reference. The chosen strategy
is recorded on every payroll run and in the calculation trace, so if the interpretation is later
corrected, affected periods can be identified precisely and recalculated deliberately.

Proposed seed default: `AggregateInPrimaryCurrency` (position A), because it is the more
conservative of the two — it does not duplicate the tax-free threshold — and because it matches
the position attributed to ZIMRA guidance. **It ships as `Unverified` and requires sign-off from
the company's tax advisor before a live run** (Q1).

Related unresolved points: which exchange rate (interbank / official / RBZ published), and as at
which date (date of payment / period end / date of accrual). Modelled as
`CurrencyTaxStrategies.RateDeterminationRule` and `RateType`, seeded `Unverified`.

---

## 4. Employee types — behavioural matrix

Every column below is a **default** written into `EmploymentTypes`, overridable per employee.
Nothing here is hard-coded in the engine.

| # | Type | Default frequency | Earnings basis | Contract end required | Timesheet | Leave accrual | NSSA default | PAYE | Notes |
|---|---|---|---|---|---|---|---|---|---|
| 1 | Permanent | Monthly | Monthly salary | No | Optional | Yes | Eligible | Standard | Baseline |
| 2 | Contract | Monthly | Monthly salary | **Yes** | Optional | Yes | Eligible | Standard | Expiry warning |
| 3 | Project-based | Per project / monthly | Project rate, or daily × days | Yes (project end) | **Yes** | Configurable | Eligible (verify) | Standard | Must be assigned to a project; cost rolls up to project reports |
| 4 | Temporary | Monthly / weekly | Monthly or daily | Yes | **Yes** | Configurable | Verify | Standard | Watch for deemed-permanent thresholds |
| 5 | Casual | Weekly / on completion | Daily rate × days | No | **Yes** | No | **Verify — configurable** | Standard | Six-weeks-in-four-months warning |
| 6 | Occasional | Per engagement | Daily or fixed fee | No | **Yes** | No | **Verify** | Standard | Engagement-based |
| 7 | Part-time | Monthly / weekly | Hourly × hours, or pro-rata salary | Optional | **Yes** | Pro-rata | Eligible (verify) | Standard | Pro-rata ceiling treatment to confirm |
| 8 | Seasonal | Weekly / monthly | Daily or monthly | Yes | **Yes** | Configurable | Verify | Standard | Rehire pattern preserved across seasons |
| 9 | Intern / Trainee | Monthly | Stipend / monthly | Yes | Optional | Configurable | **Verify** | Standard, subject to threshold | Stipend may fall below the tax-free threshold |
| 10 | Commission-based | Monthly | Commission structure ± retainer | Optional | No | Configurable | Eligible (verify) | Standard | Commission structure configured per employee |
| 11 | Hourly-paid | Weekly / fortnightly | Hourly × hours | Optional | **Yes** | Pro-rata | Eligible (verify) | Weekly/fortnightly table | Overtime multipliers apply |

Where a cell says "Verify", the default ships `Unverified` and the warning appears on the
dashboard until an administrator confirms it against NSSA/ZIMRA guidance. **Every one of these
behaviours is data, not code**, so a legislative change means editing a rule, not a release.

**Weekly and fortnightly PAYE:** the engine supports a per-period table (`PeriodBasis`
`Weekly`/`Fortnightly`) and an annual-equivalent method. Which is correct for Zimbabwe weekly
payroll — and how a weekly-paid employee's NSSA ceiling is apportioned — is **unresolved** (Q3,
risk R-07).

---

## 5. Compliance risk register

| ID | Risk | Impact | Mitigation |
|---|---|---|---|
| **R-01** | Multi-currency PAYE methodology is genuinely contested (§3) | Under- or over-deduction across the whole workforce; penalties and interest | Configurable, versioned strategy; advisor sign-off required; recorded per run; targeted recalculation possible |
| **R-02** | Official sources unreachable during research; all seed rates unverified | Wrong tax from day one | `VerificationStatus` on every rule; dashboard warning; go-live checklist requires verification |
| **R-03** | Rates change frequently (NSSA ceiling reportedly gazetted quarterly; annual Finance Act changes) | Silent drift into non-compliance | Dated rule versions; expiry warning before the next pay date; never a fallback default |
| **R-04** | AIDS Levy base (before or after tax credits) not established | Small but systematic error for credit-entitled employees | Configurable `Base`; seeded `Unverified`; Q2 |
| **R-05** | NSSA insurable earnings definition (basic only vs. basic + allowances) not established | Wrong contributions for every employee with allowances | `EarningsBasis` configurable; per-earning-type `IsNssaApplicable` flag; Q4 |
| **R-06** | APWCS rate and base are company-specific and must come from NSSA | Understated employer liability | Rate entered from the company's NSSA assessment; blocking warning while unset |
| **R-07** | Weekly/fortnightly PAYE method and NSSA ceiling apportionment unresolved | Wrong tax for weekly-paid staff (common in construction) | Both methods implemented and selectable; Q3 |
| **R-08** | Casual employees may be deemed permanent by operation of law | Retrospective statutory and leave liabilities | Automatic threshold warning; never an automatic reclassification |
| **R-09** | Exchange rate source and determination date not established | Wrong converted tax base; audit findings | Rate type, source, date and direction all recorded and frozen per run |
| **R-10** | Personal data handling under the Cyber and Data Protection Act | Regulatory exposure | RBAC, encryption at rest option, audit log, configurable retention; legal review before go-live |
| **R-11** | Bonus/retrenchment exemption limits not established | Over-taxing a bonus month | `TaxExemptionRules` configurable; seeded empty rather than guessed |
| **R-12** | NEC obligations vary by industry and were not established | Missed deductions/remittances for e.g. construction | Generic `EmployerLevyRules` + deduction types; company supplies its NEC schedule |
| **R-13** | Minimum wage / NEC-set minimum rates not established | Underpayment | Optional configurable minimum-rate warning per employment type and industry |

**Standing principle:** where a rule is uncertain, the system raises a visible, blocking or
warning-level flag. It does not invent a rule, and it does not quietly pick a default that
happens to produce a plausible number.

---

## 6. Sources consulted

Accessed 2026-09-12 via web search. **Secondary sources — not authoritative.** The official
ZIMRA and NSSA sites were blocked by network policy and could not be consulted directly.

- ZIMRA — Pay As You Earn (PAYE) System: https://www.zimra.co.zw/domestic-taxes/individual/pay-as-you-earn-paye *(blocked — must be read before go-live)*
- NSSA — Contributions: https://www.nssa.org.zw/contributions/ *(blocked — must be read before go-live)*
- Zimbabwe Tax Tables 2026 — ZIMRA PAYE Rates, USD & ZiG Brackets: https://registercompany.co.zw/zimra/income-tax
- PAYE Tax Tables Zimbabwe 2026: https://zimtax.co.zw/
- Zimbabwe Payroll Guide — Taxes & Compliance (Multiplier): https://www.usemultiplier.com/zimbabwe/payroll
- Zimbabwe payroll guide 2026: PAYE, NSSA and employer obligations (AnooreHR): https://anoorehr.com/blog/zimbabwe-payroll-guide-2026
- Zimbabwe PAYE tax 2025: ZiG bands & dual currency pay (Workforce Africa): https://workforceafrica.com/zimbabwe-paye-tax-2025-zig-payroll-guide/
- NSSA Rates 2025: updated pension and accident prevention scheme contributions (M&J Consultants): https://mjconsultants.co.zw/insights/nssa-rates-2025-updated-pension-and-accident-prevention-scheme-contributions/
- PAYE, NSSA and ZIMDEF: understanding Zimbabwe's statutory payroll deductions (M&J Consultants): https://mjconsultants.co.zw/insights/paye-nssa-and-zimdef-understanding-zimbabwes-statutory-payroll-deductions/
- Standards Development Fund Levy in Zimbabwe (M&J Consultants): https://mjconsultants.co.zw/standards-development-fund-levy-in-zimbabwe/
- Demystifying the NSSA Accident Prevention and Workers Compensation Scheme (Lucent Consultancy): https://lucent.co.zw/regulations/demystifying-the-nssa-accident-prevention-and-workers-compensation-apwc-scheme/
- NSSA cracks down on non-compliance: daily penalties for late P4 returns (Lucent Consultancy): https://lucent.co.zw/bookkeeping/nssa-cracks-down-on-non-compliance-employers-face-crippling-daily-penalties-for-late-p4-returns/
- NSSA — Pension & Other Benefits (POBS) (Belina Payroll knowledge base): https://belinapayroll.freshdesk.com/support/solutions/articles/72000596979-nssa-pension-other-benefits-pobs-
- Tax credits available to individuals in Zimbabwe (Mondaq): https://www.mondaq.com/tax-authorities/1098164/tax-credits-available-to-individuals-in-zimbabwe
- Understanding tax credits available to individuals in employment in Zimbabwe (Lucent): https://lucent.co.zw/tax/understanding-tax-credits-available-to-individuals-in-employment-in-zimbabwe/
- Labour Act Chapter 28:01, updated to 2019 (Veritas Zimbabwe): https://www.veritaszim.net/sites/veritas_d/files/Labour%20Act%20updated%20to%202019.pdf
- ZIMDEF Manpower Training Levy (ZIMDEF / Manpower Planning & Development Act Ch. 28:02 s.53, SI 74 & 392 of 1999): https://zimdef.org.zw/faq/
- Zimbabwe tax compliance calendar 2026: https://registercompany.co.zw/tools/tax-calendar
- Zimbabwe leave entitlements (RegisterCompany): https://registercompany.co.zw/employment/leave-entitlements
