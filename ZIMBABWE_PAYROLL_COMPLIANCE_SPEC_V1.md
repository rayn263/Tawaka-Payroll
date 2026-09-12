# ZIMBABWE PAYROLL COMPLIANCE SPECIFICATION v1.0

**Status:** DRAFT — awaiting approval. Not yet authoritative.
**Research date:** 12 September 2026
**Scope:** Statutory basis for the Tawaka Payroll calculation engine.
**Supersedes:** the compliance section of `docs/COMPLIANCE_ZIMBABWE.md` (that document remains as
the architectural risk register; this document governs calculation).

---

## 0. RESEARCH INTEGRITY STATEMENT — READ BEFORE USING THIS DOCUMENT

### 0.1 What could not be done

The research standard you set requires primary sources: legislation, ZIMRA publications, NSSA
publications, Treasury. **None of them could be opened from this environment.**

Direct connection attempts were made and logged. Every one returned `HTTP 403 Forbidden` from the
organisation's network egress proxy at the CONNECT stage — the request never reached the host:

| Host | Result |
|---|---|
| `zimra.co.zw`, `www.zimra.co.zw` | 403 — blocked by egress policy |
| `nssa.org.zw`, `www.nssa.org.zw` | 403 — blocked |
| `veritaszim.net` (legislation repository) | 403 — blocked |
| `zimlii.org`, `africanlii.org` (Legal Information Institutes) | 403 — blocked |
| `rbz.co.zw` (Reserve Bank — exchange rates) | 403 — blocked |
| `zimtreasury.gov.zw`, `parlzim.gov.zw` | 403 — blocked |
| `zimdef.org.zw` | 403 — blocked |
| `taxsummaries.pwc.com` | 403 — blocked |

This is an organisation policy denial, not a transient network fault, and per the environment's
own operating rules it must be reported rather than worked around. **I did not attempt to
circumvent it.**

The only research channel available was a web search tool that returns extracted snippets from
indexed pages, including from ZIMRA's own site. That channel is genuinely useful — several
findings below are quoted from ZIMRA and NSSA pages and from the Labour Act — but it is
**transmission through a third party of text I could not verify against the live document**. A
snippet can be stale, truncated, or drawn from a superseded page.

### 0.2 Consequence for the confidence system

Applying your standard honestly:

> **No rule in this specification is marked 🟢 VERIFIED.**

🟢 requires reading the official source. That was impossible. The highest grade awarded here is
🟡 SUPPORTED, and I have split it into two sub-grades so the verification effort can be
prioritised rather than treated as one undifferentiated pile:

| Grade | Meaning | Engine behaviour |
|---|---|---|
| 🟢 **VERIFIED** | Official source read directly, document and date recorded | Usable in live payroll |
| 🟡**+** **SUPPORTED (official text, indirectly obtained)** | Snippet attributed to ZIMRA, NSSA or legislation, consistent across sources, but the document was not opened | Blocked from live payroll until signed off per rule; one verification visit each |
| 🟡 **SUPPORTED (professional consensus)** | Multiple credible professional sources agree; no official text obtained | Blocked from live payroll; verification required |
| 🔴 **UNVERIFIED** | Single weak source, sources conflict, or no evidence | **Hard-blocked. Engine refuses to calculate.** |

**Engine rule (non-negotiable, ADR-012):** a payroll run in LIVE mode resolves only 🟢 rules. Any
🟡 or 🔴 rule required by a run raises a blocking error naming the rule, its grade and what must
be done to clear it. 🟡 and 🔴 rules calculate only in TEST mode, which watermarks every payslip
and report `TEST — NOT FOR STATUTORY USE` and refuses to create statutory obligations.

This means: **on the evidence available, this system cannot yet run a live Zimbabwean payroll.**
That is the correct outcome of a verification gate, not a failure of it. §26 sets out the shortest
path to clearing it — approximately one working day of someone opening ten documents.

### 0.3 What this document is worth despite that

It is a complete, structured verification target. Every rule below carries the exact document to
open and the exact value to confirm. Section 26 is a checklist that converts this document from
🟡 to 🟢 without further research effort. The 34 test cases in §24 are written so that the
arithmetic ones recompute automatically once the tables are confirmed.

---

## 1. PAYE

### 1.1 Legislative basis

| Item | Finding | Grade |
|---|---|---|
| Governing Act | Income Tax Act **[Chapter 23:06]**. PAYE operates under the Thirteenth Schedule; employees tax arises on "remuneration" | 🟡+ |
| Rate-setting instrument | Annual **Finance Act**; tables issued by ZIMRA as public notices, effective 1 January – 31 December | 🟡+ |
| Table publication | ZIMRA publishes tax tables at `zimra.co.zw/domestic-taxes/tax-tables`, separately for **USD** and **ZWG** | 🟡+ |

### 1.2 Table structure — important implementation finding

ZIMRA tables are **not** a plain "tax each slice" ladder. They are published in
`multiply-then-subtract` form:

> **Tax = (Gross for the period × Rate for the band) − Fixed deduction for that band**

The fixed-deduction column ("less" column) is what makes a single multiplication equal the
cumulative progressive result. Both forms give identical answers if the deduction column is
correct, but **the engine must store and apply the official deduction column**, not a
reconstruction of it, so that results reconcile line-for-line with ZIMRA's own tables and with
what an auditor or ZIMRA officer will recompute.

`TaxBrackets.FixedDeductionAmount` already exists in the schema for exactly this. Confirmed as
required, not optional.

Snippet evidence of the daily ZWG table's form: *"$100 per day 18.41 The tax will be calculated
thus"* — consistent with a rate-and-deduction layout.

### 1.3 USD remuneration — tax year 2026

| Period basis | Band | Rate | Fixed deduction |
|---|---|---|---|
| **Monthly** | 0.01 – 100.00 | 0% | — |
| | 100.01 – 300.00 | 20% | **UNKNOWN — must be read from the table** |
| | 300.01 – 3,000.00 | 25% | **UNKNOWN** |
| | 3,000.01 and above | 40% | **UNKNOWN** |
| **Annual** | 0.01 – 1,200.00 | 0% | — |
| | 1,200.01 – 3,600.00 | 20% | UNKNOWN |
| | 3,600.01 – 36,000.00 | 25% | UNKNOWN |
| | 36,000.01 and above | 40% | UNKNOWN |

- Bands: 🟡 (professional consensus, multiple independent sources agree, monthly and annual
  figures are internally consistent at ×12)
- Fixed deduction column: 🔴 **UNVERIFIED — values not obtained**
- Tax-free threshold: USD 100/month, USD 1,200/year — 🟡
- Top marginal rate 40%; with AIDS Levy an effective 41.2% — 🟡 (consistent with levy-on-tax)

**Rule ID `PAYE-USD-2026`. Grade: 🔴 overall**, because a table missing its deduction column is
not a usable table. This is the single highest-value verification item.

### 1.4 ZiG (ZWG) remuneration — tax year 2026

| Period basis | Band (ZWG) | Rate | Fixed deduction |
|---|---|---|---|
| **Annual** | 0 – 33,600 | 0% | — |
| | 33,600.01 – 100,800 | 20% | UNKNOWN |
| | 100,800.01 – 1,008,000 | 25% | UNKNOWN |
| | 1,008,000.01 and above | 40% | UNKNOWN |
| **Monthly** | 0 – 2,800 | 0% | — |
| | remaining bands | as above ÷ 12 | UNKNOWN |

- Grade: 🟡 (fewer corroborating sources than USD; the 2,800/month threshold is arithmetically
  consistent with 33,600/year, which is mild internal corroboration only)
- ZIMRA reportedly published **separate USD and ZiG schedules in December 2024**, with the older
  ZWL bands obsolete — 🟡
- **The ZiG bands are NOT the USD bands converted.** They are set independently in ZWG. The engine
  must never derive one from the other. 🟡+

**Rule ID `PAYE-ZWG-2026`. Grade: 🔴 overall** (deduction column unknown; band corroboration
weaker than USD).

### 1.5 2026 changes

The 2026 National Budget reporting covers VAT (15% → **15.5%** from 1 Jan 2026), IMTT on ZiG
transactions (2% → 1.5%), a 20% Special Capital Gains Tax, and interest deductibility. **No
change to PAYE tables, bonus exemption or tax credits was reported.** 🟡

Implication: the 2026 PAYE tables may be unchanged from 2025 — but "no change was reported" is
not "no change occurred". Treat as 🔴 until the 2026 table itself is read.

### 1.6 Calculation sequence (structure 🟡+, inputs 🔴)

```
1. Gross remuneration for the period (cash + benefits + allowances)
2. LESS exempt income (e.g. bonus up to the exempt limit)
3. LESS allowable deductions (NSSA employee contribution; approved pension)
   = TAXABLE INCOME
4. Apply the tax table for the period basis and currency
   (gross × rate − fixed deduction)
   = TAX BEFORE CREDITS
5. LESS tax credits (capped at tax; no refund of excess)
   = TAX AFTER CREDITS
6. PLUS AIDS Levy 3% × tax after credits
   = PAYE PAYABLE
```

Steps 1–3 and 5–6 are 🟡+ (attributed to ZIMRA guidance: *"Calculate 3% AIDS Levy and add to tax
after credits to get actual tax payable"*). Step 4's inputs are 🔴.

### 1.7 Final Deduction System (FDS)

Zimbabwe operates an FDS under which the employer's monthly deductions are intended to be the
employee's final tax, with a year-end reconciliation. References to "final deduction system —
year end procedures" appear on ZIMRA's site. **Whether FDS cumulative reconciliation is mandatory
for this employer, and how mid-year joiners/leavers are handled, is 🔴 UNVERIFIED.**

Engine consequence: `TaxRules.CalculationMethod` already supports `PeriodTable` and
`CumulativeAnnual`. Seed `PeriodTable`; do not implement cumulative reconciliation until the FDS
requirement is confirmed (**new blocking question Q21**).

---

## 2. DUAL-CURRENCY / MIXED-CURRENCY PAYROLL

The highest-priority question, and the position has materially improved since the last document.

### 2.1 Evidence obtained

Two statements attributed to ZIMRA public notices:

> **(i)** "PAYE remains payable in local currency for remuneration in local currency, and in
> foreign currency for remuneration in foreign currency **or where there is a combination of
> foreign and local currency**."

> **(ii)** "The remuneration in RTGS$ and foreign currency **should be added then PAYE determined
> based on that total amount**."

Against one contrary professional statement:

> **(iii)** "Each element is taxed in its own currency… never convert one currency to the other
> for calculation purposes."

And one observation of market practice:

> **(iv)** "An employee paid partly in USD and partly in ZiG can, **in practice**, be taxed
> against two different band structures."

### 2.2 Assessment

(i) and (ii) are attributed to ZIMRA and are mutually consistent: **aggregate, then determine
PAYE on the total**. (iii) is a vendor's operational description, and (iv) explicitly describes
*practice*, not the rule — which is evidence that the contrary approach exists in the market, not
that it is correct.

Critically, (i) addresses remittance, not computation: tax follows the currency of the
remuneration for *payment* purposes. So the two are not in conflict once separated:

- **Computation:** aggregate (per ii)
- **Remittance:** split by currency (per i)

This is the aggregate-then-apportion model already designed into the architecture. The evidence
now supports it rather than merely permitting it.

**The caveat that keeps this 🟡, not 🟢:** statement (ii) refers to **RTGS$**, a currency that
preceded ZiG. It is probably from the 2019–2023 era. Whether ZIMRA restated the rule for ZiG
after the February 2024 currency change is **unconfirmed**, and that is exactly the sort of thing
that changes with a currency reform.

### 2.3 Scenario determinations

| Scenario | Determination | Grade |
|---|---|---|
| **A — wholly USD** | USD tables, USD remittance to ZIMRA foreign currency account | 🟡+ |
| **B — wholly ZiG** | ZWG tables, ZiG remittance to domestic taxes account | 🟡+ |
| **C — USD salary + ZiG allowance** | Aggregate into one tax base; apportion resulting tax by currency for remittance | 🟡 |
| **D — ZiG salary + USD allowance** | Same treatment as C | 🟡 |
| **E — different currencies within one period** | Same treatment as C; every component retains its original currency, amount, rate and rate date | 🟡 |
| **F — salary changes USD → ZiG mid-year** | Each period taxed under the rule and currency applying to that period. Prior periods are never restated. Year-end ITF16 filed **separately per currency**. | 🟡 |

**Unresolved within the aggregate model (all 🔴):**

1. **Direction:** is ZiG converted into USD, or USD into ZiG? Source (ii) does not say. It matters:
   the two produce different results because the USD and ZiG bands are independently set.
2. **Which table applies to the aggregate** — the currency of the majority component, of the basic
   salary, or a declared primary currency?
3. **Threshold handling:** is the tax-free threshold granted once against the aggregate (the whole
   point of aggregating), or once per currency stream?
4. **Credit handling:** credits are denominated in USD 75/month; against an aggregated base, are
   they applied once in the tax-base currency?

### 2.4 Recommended interpretation (for approval, not for silent adoption)

> Aggregate all remuneration into the employee's **declared primary payroll currency**, using the
> rule-defined exchange rate; apply that currency's table once; apply credits once; compute the
> AIDS Levy once on tax after credits; then apportion total PAYE + AIDS Levy across the source
> currencies **pro rata to each currency's contribution to the aggregated taxable income**, and
> remit each portion in its own currency to the correct ZIMRA account.

Reasoning: it follows the aggregation instruction attributed to ZIMRA; it grants the threshold
once, which is the conservative direction (a taxpayer-favourable double threshold is the error
that attracts penalties); and the pro-rata split satisfies the per-currency remittance
requirement. It is conservative where the evidence is silent.

**It must not be enabled until an advisor signs it off.** `CurrencyTaxStrategies.ApprovedByAdvisor`
gates it, and the strategy name and version are written into every calculation trace, so if the
interpretation is later corrected, affected runs are identifiable by query and can be recalculated
deliberately.

### 2.5 Reporting

| Requirement | Finding | Grade |
|---|---|---|
| Remittance accounts | USD → ZIMRA foreign currency account; ZiG → domestic taxes account | 🟡 |
| Monthly return | Per currency | 🟡 |
| Annual ITF16 | **Separate ITF16 per currency** where employees were paid in more than one currency | 🟡 |
| Penalties | Applied separately per currency stream | 🟡 |

---

## 3. EXCHANGE RATES

| Item | Finding | Grade |
|---|---|---|
| Rate for converting remuneration | "Prevailing exchange rate **on the day of payment**" (also stated for tax credits: "converted at the prevailing interbank exchange rate on the date of payment") | 🟡 |
| Rate for benefits valuation | Motor vehicle deemed benefit stated in USD "or the ZiG equivalent at the **prevailing interbank rate**" | 🟡 |
| Rate for allowable deductions (income tax, not PAYE) | Election between the **average auction rate** for the year, or the **spot rate on the transaction date**; provisional taxpayers must use the average auction rate. ZIMRA Public Notice 20 of 2023 | 🟡 |
| Official rate source | RBZ interbank rate is the referenced benchmark; ZIMRA also publishes rates for customs and income tax purposes | 🟡 |
| Rate frequency | ZIMRA publishes periodic rate schedules; RBZ publishes daily | 🟡 |

**Unresolved (🔴):** whether the PAYE-relevant rate is the RBZ interbank mid rate, a ZIMRA-published
rate, or the rate the employer actually transacted at; and whether "date of payment" means the pay
date, the date funds cleared, or the period end.

**Design consequence — implemented regardless of which answer applies.** Every converted figure
persists all seven fields you specified:

```
OriginalAmount · OriginalCurrency · ExchangeRate · RateDate · RateSource
    · ConvertedAmount · TaxCurrencyBasis
```

plus `RateType`, `ExchangeRateId` and `RateDeterminationRule`. The rate is **frozen into the
payroll run at calculation**. Editing a rate afterwards cannot alter a calculated run: a
recalculation is an explicit, permissioned, audited act that produces a diff report. Historical
integrity does not depend on getting the rate policy right first time — only on never overwriting.

---

## 4. AIDS LEVY — **RESOLVED (subject to verification)**

| Item | Finding | Grade |
|---|---|---|
| Rate | **3%** | 🟡+ |
| Base | **Income tax payable AFTER deducting credits** | 🟡+ |
| ZIMRA wording | "Tax credits are deducted (elderly, blind, disabled, medical credit), then a 3% AIDS Levy is calculated and **added to the tax after credits** to get the actual tax payable" | 🟡+ |
| Applies to | The income tax amount, not to gross or taxable income | 🟡+ |
| USD treatment | 3% of USD tax after credits; remitted in USD | 🟡 |
| ZiG treatment | 3% of ZiG tax after credits; remitted in ZiG | 🟡 |
| Mixed currency | Computed once on aggregate tax after credits, then apportioned with PAYE (§2.4) | 🟡 |
| Deadline / return | With PAYE, 10th of the following month, same return | 🟡 |
| Qualification | No qualification, exclusion or cap found. Absence of evidence — 🔴 that none exists | — |

**This resolves original blocking question Q2.** The earlier ambiguity is closed: the levy is
charged on tax *after* credits, which is the taxpayer-favourable direction and matters materially
for any employee with the USD 75/month elderly or disabled credit.

`AidsLevyRules.Base` remains configurable — but its seed value is now evidence-based rather than
a guess.

---

## 5. NSSA — PENSION AND OTHER BENEFITS SCHEME (POBS)

### 5.1 Contributions

| Item | Finding | Grade |
|---|---|---|
| Employee rate | **4.5%** | 🟡+ |
| Employer rate | **4.5%** | 🟡+ |
| Total | 9% | 🟡+ |
| Ceiling | **USD 700/month**, or the ZWG equivalent | 🟡 |
| Max employee contribution | USD 31.50/month | 🟡 |
| Ceiling review frequency | Reportedly **gazetted quarterly** | 🟡 |
| Legal instrument | **SI 393 of 1993** (NSSA POBS Scheme) | 🟡+ |

The quarterly review is an operational risk of the first order: a ceiling that changes four times
a year and is missed produces four quarters of wrong contributions. `NssaRules` is dated, and the
dashboard raises "statutory rule expiring" before the next pay date.

### 5.2 Insurable earnings — **RESOLVED in structure, ambiguous in threshold**

| Item | Finding | Grade |
|---|---|---|
| Base | **Basic salary** | 🟡 |
| Gross-up rule | Where basic is below the ceiling and regular allowances/benefits are large relative to basic, the allowances and benefits are **grossed up and deemed to be salary** for establishing insurable earnings. Cited as **SI 393/93 section 12** | 🟡 |
| Excluded | **Overtime, bonuses, non-cash benefits** | 🟡 |
| Included | Basic salary; regular allowances where the gross-up rule bites | 🟡 |

**The ambiguity, stated precisely.** Two formulations of the same trigger appeared, and they are
not the same threshold:

- (a) "if the combined total of regular allowances and regular benefits **is equal to or more than
  the basic salary**" → trigger at allowances ≥ 100% of basic
- (b) "where allowances and other benefits **exceed the basic salary by 100%**" → trigger at
  allowances ≥ 200% of basic

For an employee on basic 400 with allowances 500: under (a) the gross-up applies; under (b) it
does not. Different contributions, for both employee and employer.

**Engine design:** `NssaRules.EarningsBasis` plus a `GrossUpTriggerMultiplier` (1.0 or 2.0) and a
per-earning-type `IsNssaApplicable` flag with an `IsRegular` marker. Seed `BasicOnly` with the
gross-up **disabled** and graded 🔴, because applying the wrong threshold silently is worse than
declining to apply it.

**This partially resolves Q4:** the base is basic salary, and overtime/bonuses are excluded —
which is a real answer. The gross-up threshold remains open as **Q4a**.

### 5.3 Coverage and eligibility — **RESOLVED (subject to verification)**

| Category | Determination | Grade |
|---|---|---|
| Age | **16 to under 65.** Contributions **cease** on attaining 65 even if employment continues | 🟡+ |
| Covered | Permanent, **seasonal, contract and temporary** employment — compulsory under SI 393/1993 | 🟡+ |
| **Casual** | Employees contracted for **fewer than 18 days in a month do not contribute** | 🟡 |
| Excluded | **Domestic employees**; informal sector | 🟡+ |
| Exempt | Non-Zimbabwean citizens not ordinarily resident; non-Zimbabwean diplomatic staff | 🟡 |

**This substantially resolves Q5.** The 18-day test is a concrete, implementable rule and answers
casual treatment directly. Project-based, part-time and intern employees are not separately
addressed in the evidence; by the logic of the rules found, they are covered if they meet the age
test and are not domestic/informal, with the 18-day test applying where engagement is irregular.
That inference is **🔴 and flagged** — `NssaEligibilityRules` carries a row per employment type,
and unverified rows block live payroll for employees of that type only, not the whole run.

### 5.4 Non-monthly payroll

| Item | Finding |
|---|---|
| Is the ceiling monthly? | Yes — USD 700 **per month** 🟡 |
| Weekly application | 🔴 **UNVERIFIED.** No evidence on whether the ceiling is pro-rated per week, applied monthly across weekly runs, or applied per run |
| Fortnightly application | 🔴 **UNVERIFIED** |
| 18-day casual test | Expressed in days per **month**; how it applies across weekly runs is 🔴 |

Three candidate methods (pro-rate the ceiling by period length; accumulate within the calendar
month and cap at 700; apply 700 per run) give materially different answers, and applying 700 per
weekly run would understate contributions roughly four-fold. `NssaRules.CeilingPeriodBasis` holds
the choice; seeded 🔴. **New blocking question Q22.**

---

## 6. NSSA — APWCS (ACCIDENT PREVENTION AND WORKERS COMPENSATION SCHEME)

| Item | Finding | Grade |
|---|---|---|
| Who pays | **Employer only.** Nothing deducted from the employee | 🟡+ |
| Rate basis | Per-employer rate set from **industry risk classification** | 🟡+ |
| Wage base | **Uncapped basic wages** — no ceiling equivalent to the POBS USD 700 | 🟡 |
| Indicative rates | Low risk (retail, education, finance) ≈ **0.5%–1%**; high risk (**mining, construction**) several percentage points higher | 🟡 |
| Assessment mechanism | **Form WC50** — annual return used to assess the employer's risk and set the rate for the following year | 🟡 |
| Rate publication | Rates issued via NSSA / Government Printers (Printflow), typically at year end | 🟡 |
| Construction treatment | High-risk classification; rate specific to the employer's assessment | 🟡 |

**This company's rate cannot be derived, inferred or estimated.** It is assigned by NSSA to this
employer, for this industry code, following the WC50 assessment.

**Engine design:** `ApwcsRules` starts **empty**. The engine does not default to a rate, does not
use an "industry average", and does not skip the cost silently — it raises a blocking error:
*"APWCS rate not configured. Obtain your assessed rate and industrial classification from NSSA
(Form WC50) and enter it in Settings → APWCS Configuration."* The uncapped basic-wage base is
seeded but graded 🟡. **Q6 stands open — only NSSA can close it.**

---

## 7. ZIMDEF (MANPOWER DEVELOPMENT LEVY)

| Item | Finding | Grade |
|---|---|---|
| Applies | **Yes** — all registered employers paying remuneration | 🟡+ |
| Rate | **1%** of the gross wage bill (leviable items) | 🟡+ |
| Legal basis | Manpower Planning and Development Act **[Chapter 28:02] s.53**; **SI 74 and SI 392 of 1999** | 🟡+ |
| Borne by | **Employer.** Not an employee deduction | 🟡+ |
| Wage base | Inclusive of allowances, bonuses, benefits, employer NSSA, medical aid, NEC and pension contributions | 🟡 |
| Deadline | **15th** of the following month | 🟡 |
| Exemptions | 🔴 **None found — absence of evidence, not evidence of absence** |
| Rebates | Training rebates reportedly available (recovery of levy against approved training) | 🟡 |

**Note the deadline conflict:** ZIMDEF is reported as the **15th** while PAYE, NSSA and SDF are the
**10th**. A single "statutory deadline = 10th" assumption would make ZIMDEF appear overdue five
days early every month. Each obligation type therefore carries its **own** `DueDateRule`. Both
dates are 🟡 and on the verification list.

The wage base is notably **wider than the PAYE base** — it includes employer NSSA and pension
contributions, which are not employee remuneration. The engine computes a distinct
`LeviableWageBill` aggregate rather than reusing gross pay. `EmployerLevyRules.Base` supports
`GrossWageBill` / `LeviableWageBill` / `BasicEarnings`.

---

## 8. STANDARDS DEVELOPMENT FUND (SDF)

| Item | Finding | Grade |
|---|---|---|
| Applies | Reportedly **all employers, with very few exceptions**; "if you employ staff and remit PAYE, you are generally liable" | 🟡 |
| Rate | **0.5%** of the monthly gross wage bill | 🟡 |
| Borne by | Employer | 🟡 |
| Deadline | **10th** of the following month | 🟡 |
| Legal basis | 🔴 **Not established.** No Act or SI reference obtained |
| Exemptions | 🔴 Not established |
| Industry applicability | 🔴 Not established — "very few exceptions" is not a rule the engine can apply |

You were right to flag the 0.5% figure. It is corroborated only by the professional-consensus
tier, with **no statutory anchor found**, and is materially weaker evidence than ZIMDEF.

**Engine design:** SDF ships **configured but inactive**, graded 🟡 with no legal basis recorded.
Activating it is a deliberate administrator act after confirming liability. The system does not
levy a charge on a business that may not owe it.

---

## 9. NEC / COLLECTIVE BARGAINING LEVIES

| Item | Finding | Grade |
|---|---|---|
| Mechanism | Industry CBAs registered as **Statutory Instruments** under the Labour Act [Cap 28:01], binding on all employers in the industry | 🟡+ |
| Typical structure | Monthly levies of roughly **2%–4% of the basic wage bill**, commonly split **50/50** employer/employee | 🟡 |
| Worked examples found | Chemicals/Fertilisers/Battery: 2% total (1% employer + 1% employee). Plastics: 1.5% + 1.5%. Energy: 0.8% of basic, employer matching each dollar | 🟡 |
| Construction NEC | Exists — **NEC for the Construction Industry**, CBA between the Construction Industry Federation of Zimbabwe / Zimbabwe Building Contractors Association and the Zimbabwe Construction and Allied Trade Workers Union. Instruments identified: **SI 112 of 2021**, **SI 111 of 2021**, earlier SI 45 of 2013 | 🟡+ |
| **Construction levy rate** | 🔴 **NOT OBTAINED.** The SI documents are hosted on a blocked domain |
| Construction minimum wages | 🔴 Not obtained — set by the same CBA and relevant to underpayment warnings |

**Engine design — exactly the configurable structure you specified, and no automatic deduction:**

```
NecSchemes: Industry · NecName · SiReference · EffectiveFrom/To · VerificationStatus
NecRates:   NecSchemeId · EmployeeRate · EmployerRate · Base (BasicWage|GrossWage|FlatAmount)
            · FlatAmount · Currency · EffectiveFrom/To
NecEligibility: EmploymentTypeId · IsIncluded · GradeOrCategory
Employee opt-in: EmployeeStatutoryProfiles.NecMembershipNumber + IsNecMember
```

**No employee has a NEC deduction unless that employee is explicitly marked a member of a
configured, dated NEC scheme.** There is no global default and no industry-wide auto-apply.

**New blocking question Q23** (only if this business is in construction): obtain SI 112 of 2021
(and any later amending CBA) from NECCIZ and extract levy rates, base, split and minimum wage
grades.

---

## 10. EMPLOYMENT CLASSIFICATION

### 10.1 The substance-over-label principle — stated plainly, as you asked

Zimbabwean employment status is determined by the **actual nature and duration of the engagement**,
not by the label on the contract. Section 12(3) of the Labour Act converts a casual worker to a
contract without limit of time **by operation of law** once a factual threshold is crossed —
irrespective of what the parties agreed or what the payroll system says. The Labour Amendment Act
5 of 2015 added s.12(3a), under which a contract specifying its duration (**including a contract
for casual work**) is deemed to be without limitation of time upon expiry of a continuous service
period fixed by the appropriate employment council or prescribed by the Minister. 🟡+

**Therefore: the `EmploymentType` field in this system is an administrative classification for
payroll processing. It is not a legal determination and the system must never present it as one.**
The engine raises warnings where the facts suggest the legal position has diverged from the label;
it never reclassifies anyone.

### 10.2 Matrix

| Type | PAYE | NSSA POBS | Leave | Employer obligations | Contract requirement |
|---|---|---|---|---|---|
| **Permanent** | Standard tables | Covered (16–<65) 🟡+ | Full statutory | All | Without limit of time |
| **Fixed-term contract** | Standard | Covered ("contract employment" named) 🟡+ | Full, usually pro-rated | All | Written, with end date; s.12(3a) risk on renewal |
| **Project-based** | Standard | 🔴 Inferred covered | 🔴 Configurable | All | Tied to project duration |
| **Temporary** | Standard | Covered ("temporary employment" named) 🟡+ | Pro-rata | All | Written |
| **Casual** | Standard, per pay-period table | **<18 days/month → no contribution** 🟡 | Generally none while genuinely casual | ZIMDEF/SDF apply to wage bill; APWCS applies | ≤6 weeks in any 4 consecutive months |
| **Occasional** | Standard | 🔴 Treat as casual pending verification | 🔴 | As above | Engagement-based |
| **Part-time** | Standard | 🔴 Inferred covered; ceiling treatment unclear | Pro-rata | All | Written |
| **Seasonal** | Standard | Covered ("seasonal employment" named) 🟡+ | 🔴 Configurable | All | Written, recurring |
| **Intern / Trainee** | Standard; stipend often below threshold | 🔴 Inferred covered if ≥16 | 🔴 Configurable | All | Training agreement |
| **Commission-based** | Standard on commission as remuneration | 🔴 Commission's status in insurable earnings unclear | Per contract | All | Written commission structure |
| **Hourly-paid** | Table matching pay frequency | 🔴 Ceiling apportionment unclear (§5.4) | Pro-rata | All | Written |

Where a cell is 🔴, `NssaEligibilityRules` blocks live payroll **for employees of that type only**
— a single unverified intern does not stop the whole company's payroll.

---

## 11. CASUAL EMPLOYEE RULE — **RESOLVED (wording obtained)**

**Section 12(3), Labour Act [Chapter 28:01]**, quoted as:

> "…a casual worker shall be deemed to have become an employee on a contract of employment
> **without limit of time** on the day that his period of engagement with a particular employer
> exceeds a total of **six weeks in any four consecutive months**."

Grade: 🟡+ (verbatim wording, consistent across sources; the Act itself was not opened).

Supporting definition: casual work is work for which an employee is engaged for **not more than a
total of six weeks in any four consecutive months**. 🟡+

| Question | Determination |
|---|---|
| How is the period calculated? | Aggregate **days/weeks of engagement** with the same employer within any rolling four-consecutive-month window — not calendar quarters, and not continuous service. A **rolling** window. 🟡 |
| What happens at the threshold? | Deemed, **by operation of law, on the day the threshold is exceeded**, to be an employee without limit of time. 🟡+ |
| Automatic or process required? | **Automatic** — "shall be deemed". No employer act, election or notice is required for the legal effect. What requires action is the employer's *consequential* compliance: contract, leave, notice rights, NSSA. 🟡+ |
| Related provision | s.12(3a): fixed-duration contracts, including casual work contracts, deemed without limitation of time on expiry of a continuous service period fixed by the employment council or Minister 🟡+ |

### System behaviour (specified, non-negotiable)

The engine maintains a rolling four-month engagement tally per casual employee and warns at a
configurable threshold (default 5 weeks — **before** the line is crossed, so the business can
decide rather than discover):

> ⚠ **Casual engagement threshold approaching — Tapiwa Ncube has 5 weeks 2 days of engagement in
> the four months to 12 Sep 2026. At 6 weeks, s.12(3) of the Labour Act deems the employee to be
> on a contract without limit of time. This has contractual, leave and NSSA consequences. Seek
> advice and update the employment record if appropriate.**

The system **does not** change `EmploymentType`, **does not** start accruing leave, and **does
not** alter NSSA treatment on its own. The legal deeming happens whether or not the software
notices; the software's job is to make sure a human notices. A silent reclassification would be
the system making a legal determination it has no standing to make — and, in the other direction,
suppressing the warning would leave a real liability invisible.

---

## 12. WEEKLY PAYROLL — **MATERIALLY RESOLVED**

> **Finding: official weekly tables exist.** "The tax tables have **daily, weekly, fortnightly,
> monthly and annual** categories, with the specific category to be used depending on the
> **frequency of remuneration**." ZIMRA reportedly supplies ready-made daily, weekly and monthly
> tables for **both currencies**. 🟡+

This overturns the prior working assumption that weekly PAYE might need derivation. It does not.

| Item | Determination | Grade |
|---|---|---|
| Do official weekly tables exist? | **Yes**, both currencies | 🟡+ |
| Which table applies? | The one matching the **frequency of remuneration** | 🟡+ |
| Derive weekly from monthly? | **No.** Never divide monthly by 4 or 4.33 | 🟡+ |
| Weekly band values | 🔴 **Not obtained** | |
| AIDS Levy | 3% of tax after credits, same rule, per period | 🟡+ |
| Tax credits | 🔴 Per-week apportionment of the USD 75/month credit not established | |
| NSSA ceiling | 🔴 Unresolved (§5.4) — the material weekly risk | |

**Engine design:** `TaxRules.PeriodBasis` ∈ {Daily, Weekly, Fortnightly, Monthly, Annual}; the
resolver selects strictly on the employee's payment frequency. Deriving a table for a missing
period basis is **not implemented** — a missing weekly table is a blocking error, never an
approximation. **Q3 is resolved in method; the table values remain to be read.**

---

## 13. FORTNIGHTLY PAYROLL

Same finding: a **fortnightly** category exists in the official tables. 🟡+

| Item | Determination |
|---|---|
| Method | Use the official fortnightly table |
| Divide monthly by two? | **No** — explicitly not, and the engine cannot do it |
| Band values | 🔴 Not obtained |
| NSSA ceiling | 🔴 Unresolved (§5.4) |

---

## 14. OVERTIME

| Question | Finding | Grade |
|---|---|---|
| PAYE | **Yes — fully taxable.** Overtime is remuneration | 🟡+ |
| **NSSA POBS** | **NO — overtime is excluded from insurable earnings** | 🟡 |
| APWCS | 🔴 Base described as "basic wages", which suggests exclusion, but not stated | |
| ZIMDEF | Likely included (base is the gross wage bill inclusive of allowances and bonuses) 🟡 | |
| SDF | 🔴 Base not established | |
| NEC | 🔴 Typically basic-wage-based; per CBA | |

The NSSA exclusion is the operationally significant one: **an employee with heavy overtime pays
PAYE on it but no additional NSSA.** A payroll system that naively applies NSSA to gross
over-deducts from exactly the site staff who work the most overtime.

Statutory treatment is held separately from the company's overtime **rate** policy (1.5×, 2.0×
etc.), which is a contractual/NEC matter configured in `EarningTypes.DefaultMultiplier` and is not
a statutory rule. **Q15 remains a business question, not a compliance one.**

---

## 15. BONUSES, COMMISSION AND ONCE-OFF PAYMENTS

| Item | Finding | Grade |
|---|---|---|
| **Annual bonus exemption** | First **USD 700** (or ZiG equivalent) exempt; excess added to that month's income and taxed at marginal rates | 🟡+ |
| Legal basis | **Finance Act No. 2 of 2024**, raising it from USD 400 | 🟡+ |
| History | USD 400 from 1 Jan 2024 → USD 700 late 2024 | 🟡 |
| Applies to 2026? | 🔴 No 2026 change reported, but not confirmed |
| **NSSA on bonuses** | **Excluded from insurable earnings** | 🟡 |
| Performance / project bonus | 🔴 Whether the exemption covers **any** bonus or only a 13th-cheque annual bonus is **not established** |
| Commission | Taxable remuneration 🟡+. NSSA status 🔴 |
| Once-off payments | Taxable as remuneration 🟡. Retrenchment packages have their own exemption regime — 🔴 not researched, out of scope for v1 |

**Resolves an earlier confusion:** the "USD 400 tax-free threshold from 1 January 2024" that
appeared in the previous research was almost certainly the **bonus exemption**, not the PAYE
tax-free threshold (which is USD 100/month). The 400 → 700 history supports that reading. Recorded
as the probable explanation, **not** as a confirmed fact.

**Engine design:** `TaxExemptionRules` holds the limit per currency with effective dates, applied
**annually per employee** (tracked year-to-date so a bonus split across two months cannot claim the
exemption twice). Because scope is unclear, the exemption attaches to a **specific earning type**
flagged `IsExemptUpToLimit` — the administrator decides which bonus type qualifies, rather than the
engine assuming every payment named "bonus" does.

---

## 16. ALLOWANCES

| Allowance | PAYE | NSSA | ZIMDEF | Grade |
|---|---|---|---|---|
| Housing (cash) | Taxable | Regular → gross-up rule may apply | Included | 🟡 |
| Transport (cash) | **Taxable in full** — "$100 given for transport is fully added to salary and taxed" | Regular → may apply | Included | 🟡+ |
| Fuel (cash) | **Fully taxable** | 🔴 | Included | 🟡 |
| Telephone | Taxable unless reimbursive with proof | 🔴 | Included | 🟡 |
| Meal | Taxable | 🔴 | Included | 🟡 |
| Travel | Taxable **to the extent not expended on the employer's business** | 🔴 | Included | 🟡+ |
| Representation / entertainment | Taxable to the extent not expended on the employer's business | 🔴 | Included | 🟡+ |
| Subsistence | Same test | 🔴 | Included | 🟡+ |
| Responsibility / risk / project | 🔴 Not specifically addressed; general remuneration principles apply | 🔴 | Included | 🔴 |
| Medical | 🔴 Interacts with the medical credit (§18) | 🔴 | Included | 🔴 |
| School fees | 🔴 Not researched | 🔴 | 🔴 | 🔴 |
| **Reimbursements with proof** | **EXEMPT** — "any amount of reimbursement nature is exempt from tax, if proof of such an expense is provided" | Excluded | 🔴 | 🟡+ |

**The governing principle** (🟡+): allowances for representation, travelling, entertainment and
subsistence are taxable **to the extent that they are not expended on the employer's business**.
Reimbursement of a vouched business expense is exempt. Cash allowances that are not accounted for
are fully taxable. Civil-service allowances are excluded from this treatment (not relevant here).

**This is a per-transaction, evidence-dependent test that no payroll engine can decide by itself.**
Each `EarningType` therefore carries `IsTaxable`, `IsReimbursive`, `RequiresExpenseProof` and
`IsNssaApplicable`, set by the administrator per allowance. Where an allowance is marked
reimbursive, the system prompts for supporting documentation and records it against the payroll
line. **The engine does not assume all allowances are treated identically** — your instruction, and
the evidence supports it.

---

## 17. BENEFITS IN KIND

| Benefit | Valuation | Grade |
|---|---|---|
| **Motor vehicle** | Deemed benefit by **engine capacity band**, fixed USD monthly amount (or ZiG at the prevailing interbank rate), added to income before the tables apply. Example cited: 2001–3000cc ≈ USD 1,250/month | 🟡 |
| **Deemed benefit table** | 🔴 **The full engine-capacity table was not obtained** — one band only | |
| **Housing / accommodation** | Free accommodation → **open market value** is the benefit. Subsidised → the **difference** between rent paid and open market value | 🟡+ |
| **Employer loans** | Benefit = difference between interest charged and the rate prescribed by the Commissioner. Sources reference both **SOFR** and legacy **LIBOR** — 🔴 which benchmark currently applies is unresolved | 🟡 / 🔴 |
| Other benefits in kind | Taxable as remuneration under general principles | 🟡 |

**Recommendation: support benefits in kind, but not in Phase 1.** Reasons: the vehicle table is
incomplete; the loan benchmark is contested (the LIBOR→SOFR transition is exactly the kind of
change that leaves stale guidance behind); and market-value housing benefits need a valuation
workflow, not a payroll field.

Phase 1 provides the **structure** — `EarningTypes` with `IsBenefitInKind`, `ValuationMethod`
(`DeemedTable` / `OpenMarketValue` / `InterestDifferential` / `Manual`), `IsIncludedInGross`,
`IsNssaApplicable` (seeded false: non-cash benefits are excluded from insurable earnings, 🟡) —
and accepts a **manually valued** benefit amount with a note of how it was valued. The deemed-table
and loan-benefit automation land in Phase 2 once the tables are verified. A manually entered,
audited figure is honest; an automated figure from a table with one known row is not.

---

## 18. TAX CREDITS

| Credit | Amount | Grade |
|---|---|---|
| Elderly persons (age **55+**) | **USD 75/month (USD 900/year)** | 🟡 |
| Blind persons | USD 75/month (USD 900/year) | 🟡 |
| Disabled persons (mental or physical) | USD 75/month (USD 900/year) | 🟡 |
| Medical aid contributions | 🔴 **SOURCES CONFLICT** — see below | |
| Medical expense shortfall | 50% of qualifying expenses | 🟡 |

**Conflict on the medical credit:**

- Source A: "**100%** of medical aid contributions, and **50%** of other medical expenses"
- Source B: "**50%** of medical aid contributions **and** medical expense shortfalls"

Both are professional-tier sources. The difference doubles or halves the credit for every employee
on a medical aid scheme. **Graded 🔴. `TaxCreditRules.PercentageOfQualifyingAmount` is configurable
and blocks live payroll until resolved. New blocking question Q24.**

| Rule | Finding | Grade |
|---|---|---|
| **Cap** | Total credits limited to income tax chargeable; **no refund** of excess | 🟡+ |
| Currency | Denominated in USD; ZiG converted at the **prevailing interbank rate on the date of payment** | 🟡 |
| Application | Monthly (USD 75) or annually (USD 900) | 🟡 |
| Coverage | Medical credit extends to spouse and minor/step/adopted children | 🟡 |
| Residency | Disabled persons' credit reportedly unavailable to non-residents | 🟡 |
| Supporting evidence | 🔴 Documentation requirements (medical certification, age proof, scheme statements) not established |
| Interaction with AIDS Levy | Credits deducted **before** the 3% levy (§4) | 🟡+ |

`EmployeeStatutoryProfiles` already carries the entitlement flags. The engine applies credits in
the order: elderly/blind/disabled first, then medical, capping cumulatively at tax chargeable.

---

## 19. STATUTORY DEADLINES

| Obligation | Authority | Deadline | Grade |
|---|---|---|---|
| PAYE + AIDS Levy | ZIMRA | **10th** of the following month | 🟡+ |
| Monthly PAYE return (P2 / REV5) | ZIMRA | **10th** of the following month | 🟡+ |
| NSSA POBS + P4 return | NSSA | **10th** of the following month | 🟡 |
| APWCS | NSSA | 🔴 Not established (assessment is annual via WC50; premium payment frequency unclear) | |
| **ZIMDEF** | ZIMDEF | **15th** of the following month | 🟡 |
| SDF | SAZ | **10th** of the following month | 🟡 |
| NEC | Relevant NEC | 🔴 Per CBA | |
| ITF16 annual return | ZIMRA | Within **30 days** after the end of the year of assessment | 🟡+ |

**Penalties** (🟡, used only for warnings — the system never computes a penalty charge):
PAYE late payment up to 100% of tax due plus interest ~10% p.a.; late monthly return civil penalty
of the order of USD 30/day up to ~91 days; NSSA portal applies automatic penalties on late P4.

Each obligation type carries its own `DueDateRule` because they genuinely differ (10th vs 15th).
The dashboard shows, per obligation and per currency: **obligation · due date · amount · approved ·
paid · outstanding** — exactly the view you specified in §19 of your brief.

---

## 20. RETURNS AND REPORTING

| Form | Authority | Frequency | Purpose | Currency | Deadline | Grade |
|---|---|---|---|---|---|---|
| **P2** | ZIMRA | Monthly | PAYE remittance advice accompanying payment | Per currency | 10th | 🟡+ |
| **REV5** | ZIMRA | Monthly | Monthly PAYE return **filed on TaRMS** | Per currency | 10th | 🟡 |
| **ITF16** | ZIMRA | Annual | Return of employment income; per-employee totals of income, PAYE and AIDS Levy. **Separate return per currency** | Per currency | 30 days after year end | 🟡+ |
| **P6 / employee certificate** | ZIMRA | Annual | Employee's certificate of tax deducted | Per currency | Year end | 🔴 |
| **P4** | NSSA | Monthly | Contribution return, filed via NSSA Self-Service Portal | 🔴 | 10th | 🟡 |
| **WC50** | NSSA | Annual | APWCS risk assessment return; sets next year's rate | 🔴 | 🔴 | 🟡 |
| ZIMDEF return | ZIMDEF | Monthly | Levy return | 🔴 | 15th | 🟡 |
| SDF return | SAZ | Monthly | Levy return | 🔴 | 10th | 🟡 |

**Unresolved: P2 versus REV5.** One source describes P2 as the monthly return; another describes
REV5 as the monthly PAYE return on TaRMS. Most likely ZIMRA's TaRMS migration replaced or renamed
the monthly submission — but that is inference. 🔴. **New blocking question Q25.**

**Per your §20 instruction, no form-generation functionality will be built until the current
requirements are verified.** Phase 1 produces the *underlying data* (per-employee, per-currency
totals of gross, taxable, PAYE, AIDS Levy, NSSA) as exportable reports that a human can transcribe
or upload. Actual P2/REV5/ITF16/P4 file generation is deferred to Phase 3 and is explicitly
dependent on obtaining current specifications and file formats.

---

## 21. CONFIDENCE SYSTEM

As defined in §0.2. Enforcement:

```
LIVE mode  → resolver admits 🟢 only.
             Any 🟡 or 🔴 required by the run = blocking error naming the rule and remedy.
TEST mode  → 🟡 and 🔴 permitted.
             Every payslip, report and export watermarked:
                 "TEST — NOT FOR STATUTORY USE"
             Statutory obligations are NOT created. Payroll cannot be finalised or locked.
```

Blocking error text, as you specified:

> **"This statutory rule has not been verified and cannot be used for live payroll."**
> Rule: `NSSA-CEILING-2026` · Grade: 🟡 SUPPORTED · Required by: NSSA POBS calculation
> To clear: confirm the current insurable earnings ceiling from the NSSA contributions page or the
> current gazette, then record the source and date in Settings → NSSA Configuration.

Mode is a company setting; switching to LIVE is permissioned, audited, and refused while any rule
required by the active payroll calendar is below 🟢.

---

## 22. RULE VERSIONING

Every statutory rule row carries the full set you specified:

| Field | Purpose |
|---|---|
| `RuleId` | Stable identifier, e.g. `PAYE-USD-2026`, `NSSA-POBS-2026Q3` |
| `Name` | Human-readable |
| `Jurisdiction` | `ZW` (multi-jurisdiction reserved) |
| `Currency` | `USD` / `ZWG` / null where currency-neutral |
| `EffectiveFrom` / `EffectiveTo` | Dated validity; overlaps rejected on save |
| `Value` / child rows | Scalar, or bracket/credit child rows |
| `CalculationMethod` | `PeriodTable`, `CumulativeAnnual`, `PercentageOfBase`, `CappedPercentage`, `FlatAmount` |
| `Source` | e.g. "ZIMRA public notice", "SI 393 of 1993 s.12", "Finance Act No. 2 of 2024" |
| `SourceDate` | Publication date |
| `SourceReference` | URL or document reference |
| `VerificationStatus` | 🟢 / 🟡+ / 🟡 / 🔴 |
| `VerifiedBy` / `VerifiedAt` | Who confirmed it against the official source, and when |
| `Notes` | Ambiguities, interpretation taken, advisor reference |

**Immutability:** once a rule version has been used by a finalised payroll run it can never be
edited — only superseded by a new version with a later `EffectiveFrom`. Each run additionally
stores the resolved rule set as immutable JSON (ADR-004), so historical payroll reproduces exactly
even if a rule row is later corrected.

---

## 23. CALCULATION TRACE

Every figure is explainable to the level you specified. Stored in `PayrollCalculationTraces`, one
row per derived value:

```
PAYE — John Moyo — September 2026                    [computed 12/09/2026 14:32 by R. Nyakuhwa]

  Rule            PAYE-USD-2026  ·  🟡 SUPPORTED  ·  effective 01/01/2026 – 31/12/2026
  Source          ZIMRA USD monthly tax table   (verification pending — see Settings)
  Currency        USD   ·   Period basis: Monthly   ·   Strategy: SingleCurrency

  Input           Taxable income                                    USD 1,043.50
                    = Gross 1,075.00 − NSSA employee 31.50
  Bracket         300.01 – 3,000.00   →   25%   less fixed deduction [UNVERIFIED]
  Computation     ( 200.00 × 20% )                                       40.0000
                  + ( 743.50 × 25% )                                    185.8750
                                                                   ─────────────
                  Tax before credits                                    225.8750
  Credits         none applicable                                         0.0000
  Tax after credits                                                     225.8750
  Rounding        2dp, away from zero                                   USD 225.88

  AIDS Levy       AIDS-LEVY-2026 · 3% × tax after credits (225.88)       USD 6.78
  Exchange rate   n/a (single currency)
```

Clicking any figure in the preview grid or on a payslip opens this. Every element you listed —
rule, bracket, rate, input, rounding, currency, exchange rate, source — is present, plus the
verification grade, which tells the reviewer how much to trust the number.

---

## 24. TEST CASES

34 cases (30 required, 4 added for gaps the research exposed). Expected values marked
**[SEED]** are computed from the unverified seed tables in §1.3 and will change when the official
fixed-deduction column is obtained — they are written as fixtures that recompute, not as constants.
Cases marked **[BEHAVIOUR]** assert engine behaviour and are valid regardless of rate values.

Seed table used for [SEED] arithmetic: USD monthly — 0% to 100.00; 20% on 100.01–300.00; 25% on
300.01–3,000.00; 40% above 3,000.00. NSSA 4.5% each side, ceiling USD 700, basic salary only,
overtime and bonuses excluded. AIDS Levy 3% of tax after credits.

| # | Case | Input | Expected | Rule | Reason |
|---|---|---|---|---|---|
| **TC-01** | USD permanent employee | Basic 850, housing 100, transport 50, overtime 75 | Gross **1,075.00**; NSSA ee **31.50**; taxable **1,043.50**; PAYE **225.88**; levy **6.78**; net **810.84**; NSSA er **31.50** | PAYE-USD-2026 | Baseline end-to-end [SEED] |
| **TC-02** | ZiG permanent employee | Basic ZWG 12,500 | Tax computed on the **ZWG** table; result in ZWG; no USD figure anywhere in the record | PAYE-ZWG-2026 | ZiG bands are independent, never derived from USD [BEHAVIOUR] |
| **TC-03** | USD below threshold | Basic 90 | NSSA **4.05**; taxable **85.95**; PAYE **0.00**; levy **0.00**; net **85.95**. PAYE and levy lines still appear on the payslip at 0.00 | PAYE-USD-2026 | Zero-value statutory lines remain visible [SEED] |
| **TC-04** | ZiG below threshold | Basic ZWG 2,500 (< 2,800) | PAYE 0.00; NSSA applies on ZWG insurable earnings; statutory lines shown at zero | PAYE-ZWG-2026 | ZiG threshold [SEED] |
| **TC-05** | High-income USD | Basic 5,000 | NSSA **31.50** (capped); taxable **4,968.50**; PAYE **1,502.40**; levy **45.07**; net **3,421.03** | PAYE-USD-2026 | Top 40% band + ceiling [SEED] |
| **TC-06** | High-income ZiG | Basic ZWG 150,000/month | 40% band reached on the ZWG table; NSSA capped at the ZWG ceiling equivalent | PAYE-ZWG-2026 | [SEED] |
| **TC-07** | USD salary + ZiG allowance | Basic USD 800 + allowance ZWG 2,000 | **In LIVE mode: BLOCKED** — `CURRENCY-STRATEGY` unverified. In TEST mode: aggregate per strategy, one threshold, tax apportioned pro rata, both original amounts preserved with rate, rate date and source | CURRENCY-STRATEGY-2026 | Q1 unresolved — must block, not guess [BEHAVIOUR] |
| **TC-08** | ZiG salary + USD allowance | Basic ZWG 10,000 + allowance USD 100 | As TC-07, mirrored; primary currency drives the table | CURRENCY-STRATEGY-2026 | Direction symmetry [BEHAVIOUR] |
| **TC-09** | Overtime | Basic 600 + overtime 200 | PAYE on **800** gross; **NSSA on 600 only** (overtime excluded) | NSSA-EARNINGS-2026 | Overtime excluded from insurable earnings — §14 [SEED] |
| **TC-10** | Annual bonus | Basic 850 + bonus 1,000 (December) | Exempt **700**; taxable bonus **300**; NSSA insurable **700** (capped, bonus excluded → 31.50); taxable **1,118.50**; PAYE **244.63**; levy **7.34**; net **1,566.53** | BONUS-EXEMPT-2026 | Bonus exemption + NSSA exclusion [SEED] |
| **TC-11** | Allowances of mixed type | Basic 700 + transport 100 (taxable) + reimbursive travel 80 (vouched) | Gross taxable **800**; the 80 reimbursement **exempt** and excluded from taxable income but shown on the payslip | ALLOWANCE-RULES | Reimbursements exempt with proof — §16 [SEED] |
| **TC-12** | Standard NSSA | Basic 500 | NSSA ee **22.50**, er **22.50** (below ceiling, 4.5% of actual) | NSSA-POBS-2026 | Sub-ceiling proportional [SEED] |
| **TC-13** | NSSA ceiling exceeded | Basic 2,000 | NSSA ee **31.50**, er **31.50** — capped, **not** 90.00 | NSSA-CEILING-2026 | Ceiling binds [SEED] |
| **TC-14** | Casual, under 18 days | Casual, 12 days @ USD 25 | Gross **300**; **NSSA 0.00** (under 18 days in the month); PAYE per the daily or weekly table; NSSA line shown at 0.00 | NSSA-CASUAL-18DAY | 18-day rule — §5.3 [SEED] |
| **TC-14b** | Casual, 20 days | Casual, 20 days @ USD 25 | Gross **500**; **NSSA applies** (≥18 days) → 22.50 each side | NSSA-CASUAL-18DAY | Boundary on the other side [SEED] |
| **TC-15** | Project employee | Project-based, 18 days @ USD 25, project "Nyanga Shop Renovation" | Gross **450**; cost attributed to the project; appears in labour-cost-by-project | EMPLOYMENT-TYPE | Project costing [BEHAVIOUR] |
| **TC-16** | Weekly employee | Weekly, USD 150/week | Uses the **official weekly table**. Engine must **NOT** use monthly ÷ 4 or ÷ 4.33. Blocked in LIVE until the weekly table is loaded | PAYE-USD-2026-WEEKLY | §12 [BEHAVIOUR] |
| **TC-17** | Fortnightly employee | Fortnightly, USD 400 | Uses the **official fortnightly table**; never monthly ÷ 2 | PAYE-USD-2026-FORTNIGHTLY | §13 [BEHAVIOUR] |
| **TC-18** | Part-time | 0.5 FTE, basic 400 | PAYE on actual earnings; NSSA on actual basic; **ceiling not pro-rated** for a monthly-paid part-timer | NSSA-POBS-2026 | Ceiling is an earnings cap, not an FTE cap [SEED] |
| **TC-19** | Employee with a loan | Basic 850, loan instalment 100, balance 600 | Loan deducted **post-tax**; taxable unaffected; balance falls to 500; schedule row marked deducted and linked to the payslip | LOAN-DEDUCTION | Post-tax ordering [SEED] |
| **TC-20** | Employee with an advance | Basic 850, advance 200 recovered this period | Recovered post-tax; advance balance zero; net reduced by 200 | ADVANCE-RECOVERY | [SEED] |
| **TC-21** | Employee with a tax credit | TC-01 employee, aged 57 (elderly credit USD 75) | PAYE before credits **225.88**; credit **75.00**; tax after credits **150.88**; **levy 3% × 150.88 = 4.53** (not 6.78); net **888.09** | TAX-CREDIT-ELDERLY + AIDS-LEVY-2026 | Proves the levy is charged after credits — §4 [SEED] |
| **TC-22** | Zero PAYE | Basic 95 | PAYE 0.00, levy 0.00; both lines present on the payslip; **no statutory obligation row created for a zero amount** | PAYE-USD-2026 | Zero ≠ hidden, zero ≠ payable [SEED] |
| **TC-23** | Zero NSSA | Employee aged 66 | **NSSA 0.00** — contributions cease at 65; line shown at 0.00 with reason "age exemption" | NSSA-AGE-RULE | §5.3 [SEED] |
| **TC-24** | Multiple employees, mixed currencies | 3 USD + 2 ZiG employees | Two separate currency totals. **No cross-currency sum exists anywhere** in totals, reports or the dashboard | MULTI-CURRENCY | Never add USD to ZiG [BEHAVIOUR] |
| **TC-25** | Mixed-currency payroll run | One run containing both | Run finalises; **separate statutory obligations per currency** (PAYE-USD, PAYE-ZWG, NSSA-USD, NSSA-ZWG) for separate remittance | STATUTORY-OBLIGATION | Per-currency remittance — §2.5 [BEHAVIOUR] |
| **TC-26** | Historical payroll after a rate change | September run finalised under PAYE-USD-2026; a 2027 table is added; September is reopened and viewed | September still shows the **2026** figures and cites `PAYE-USD-2026` in the trace | RULE-VERSIONING | ADR-003/004 [BEHAVIOUR] |
| **TC-27** | Exchange rate changed after finalisation | Mixed-currency run finalised at 26.50; rate later updated to 28.00 | Finalised run **unchanged**; still cites 26.50 and its rate date. Recalculation is possible only by explicit reopen, and produces a diff report | EXCHANGE-RATE-FREEZE | §3 [BEHAVIOUR] |
| **TC-28** | Statutory payment outstanding | PAYE finalised, approved, not paid | Obligation: calculated ✔ deducted ✔ approved ✔ **paid ✘**; outstanding = full amount; dashboard warning; payslip shows "Remitted ✘" | STATUTORY-STATE | Never claim payment — §12 of the brief [BEHAVIOUR] |
| **TC-29** | Statutory payment completed | Payment recorded: date, method, reference, receipt | Status **Paid**; outstanding 0.00; warning cleared; **`IsPaid` cannot be set without the payment row** | STATUTORY-STATE | ADR-007 [BEHAVIOUR] |
| **TC-29b** | Partial statutory payment | PAYE 225.88; 100.00 paid | Status **PartiallyPaid**; outstanding **125.88**; warning persists | STATUTORY-STATE | Partial settlement [BEHAVIOUR] |
| **TC-30** | Locked payroll modification attempt | Locked run; attempt to edit an earning line directly via the data layer | Write **rejected by the interceptor**, not merely hidden in the UI; audit entry written for the attempt | PERIOD-LOCK | ADR-006 [BEHAVIOUR] |
| **TC-31** | 🔴 rule in LIVE mode | Any run requiring `APWCS-RATE` (unconfigured) in LIVE mode | **Blocking error** naming the rule, its grade and the remedy. Run cannot calculate | CONFIDENCE-GATE | §21 [BEHAVIOUR] |
| **TC-32** | 🔴 rule in TEST mode | Same run in TEST mode | Calculates; every payslip and report watermarked **TEST — NOT FOR STATUTORY USE**; **no statutory obligations created**; cannot finalise | CONFIDENCE-GATE | §21 [BEHAVIOUR] |
| **TC-33** | Casual six-week threshold | Casual with 5 weeks 2 days engagement in the rolling 4-month window | **Warning raised**; `EmploymentType` **unchanged**; NSSA and leave treatment unchanged | LABOUR-S12(3) | Warn, never reclassify — §11 [BEHAVIOUR] |
| **TC-34** | Missing period table | Employee paid weekly; no weekly table loaded | **Blocking error.** Engine does **not** derive weekly from monthly | PAYE-PERIOD-BASIS | §12 [BEHAVIOUR] |

---

## 25. FINAL DECISION REGISTER

### 25.1 Original six blocking questions

| # | Question | Status | Impact | Finding |
|---|---|---|---|---|
| **Q1** | Dual-currency PAYE methodology | 🟡 **ADVANCED — still OPEN** | HIGH | Evidence attributed to ZIMRA supports aggregate-then-apportion. Conversion **direction**, which table governs the aggregate, and threshold treatment remain open. Needs advisor sign-off |
| **Q2** | AIDS Levy tax base | 🟢 **RESOLVED** (pending verification) | HIGH | **3% of tax AFTER credits.** ZIMRA-attributed wording. Closed |
| **Q3** | Weekly / fortnightly PAYE method | 🟢 **RESOLVED in method** | HIGH | **Official daily, weekly, fortnightly, monthly and annual tables exist** for both currencies. Use the table matching pay frequency; never derive. Values still to be read |
| **Q4** | NSSA insurable earnings definition | 🟡 **PARTLY RESOLVED** | HIGH | Base = **basic salary**; **overtime and bonuses excluded**. Gross-up trigger ambiguous → **Q4a** |
| **Q5** | NSSA eligibility by employment type | 🟢 **LARGELY RESOLVED** | MEDIUM | Ages **16–<65**; permanent/seasonal/contract/temporary covered; **casual <18 days/month exempt**; domestic and informal excluded. Project/part-time/intern inferred → still 🔴 |
| **Q6** | APWCS rate and classification | 🔴 **OPEN — externally dependent** | HIGH | Cannot be researched. Employer-specific, assigned by NSSA via **Form WC50**. Base is uncapped basic wages 🟡 |

**Two closed, two substantially closed, one advanced, one externally dependent.**

### 25.2 New blocking questions discovered

| # | Question | Impact | Why it surfaced |
|---|---|---|---|
| **Q21** | Is the **Final Deduction System** cumulative reconciliation mandatory for this employer, and how are mid-year joiners and leavers handled? | HIGH | FDS year-end procedures found on ZIMRA; changes whether PAYE is per-period or cumulative |
| **Q22** | How is the **NSSA monthly ceiling applied to weekly/fortnightly payroll**? | HIGH | Ceiling is monthly; three plausible methods differ ~4× for weekly staff — directly affects site workers |
| **Q23** | Construction **NEC (NECCIZ) levy rates, base, split and minimum wage grades** — SI 112 of 2021 and any later CBA | HIGH *(if construction)* | CBAs are Statutory Instruments and binding; document is on a blocked domain |
| **Q24** | Medical aid credit: **50% or 100%** of contributions? | MEDIUM | Two professional sources directly conflict; doubles or halves the credit |
| **Q25** | Monthly PAYE return: **P2 or REV5** on TaRMS? Which is current? | MEDIUM | Sources describe both; affects Phase 3 return generation |
| **Q26** | **PAYE fixed-deduction ("less") column values** for every band, both currencies, every period basis | **CRITICAL** | Tables cannot be applied without it; blocks all PAYE |
| **Q27** | Employer **loan benefit** benchmark: SOFR or LIBOR? | LOW | LIBOR is being retired; sources cite both |
| **Q28** | Does the **bonus exemption** apply to any bonus, or only an annual/13th-cheque bonus? | MEDIUM | Determines whether performance and project bonuses qualify |

### 25.3 Blocking summary

**Cannot enable live payroll until resolved:** Q26 (critical), Q1, Q3-values, Q6, Q21, Q22.
**Cannot enable for affected employees only:** Q4a, Q5 (project/part-time/intern), Q23, Q24, Q28.
**Phase 3 only:** Q25, Q27.

---

## 26. VERIFICATION CHECKLIST — the shortest path from 🟡 to 🟢

Ten documents. Roughly one working day for someone with unrestricted internet access. Each line
says what to open and exactly what to extract.

| # | Document | Extract | Clears |
|---|---|---|---|
| 1 | ZIMRA tax tables page → **USD 2026 tables** (all period bases) | Every band, rate **and fixed-deduction value**, for daily/weekly/fortnightly/monthly/annual | Q26, Q3-values, `PAYE-USD-2026` |
| 2 | ZIMRA tax tables page → **ZWG 2026 tables** (all period bases) | Same | Q26, `PAYE-ZWG-2026` |
| 3 | ZIMRA **PAYE Explained** / PAYE system page | AIDS Levy wording; credit amounts and conditions; **medical credit percentage**; benefits-in-kind tables including the **full motor vehicle engine-capacity table**; loan benefit benchmark | Q2 confirm, Q24, Q27, §17 |
| 4 | ZIMRA public notice on **multiple-currency remuneration** (search "combination of foreign and local currency") | Conversion direction; which table governs; threshold treatment; whether restated for ZiG | **Q1** |
| 5 | ZIMRA **FDS year-end procedures** page | Whether cumulative reconciliation is mandatory; joiner/leaver handling | Q21 |
| 6 | **NSSA contributions page** + current ceiling gazette | Rates; ceiling and its currency; effective date; review cycle; **non-monthly application** | Q22, `NSSA-CEILING-2026` |
| 7 | **SI 393 of 1993, section 12** | Exact insurable-earnings definition and the **gross-up trigger threshold** | **Q4a** |
| 8 | **NSSA APWCS / WC50** — contact NSSA directly | This employer's **industrial classification, code and assessed rate**; premium base; payment frequency | **Q6** |
| 9 | **SI 112 of 2021** Construction Industry CBA (+ later amendments) via NECCIZ | Levy rates, base, employer/employee split, minimum wage grades | **Q23** |
| 10 | ZIMRA TaRMS guidance on monthly returns | P2 vs REV5; ITF16 per-currency requirement | Q25 |

As each is confirmed, set `VerificationStatus`, `VerifiedBy`, `VerifiedAt`, `Source` and
`SourceDate` on the affected rules. When the rules a payroll calendar needs are all 🟢, LIVE mode
unlocks for that calendar.

---

## 27. IMPLEMENTATION RULE (ADR-012)

Adopted verbatim from your §26:

> **Do not code around an unresolved statutory question.**

Concretely, the engine:

- **never** hard-codes a statutory value;
- **never** falls back to a default when a rule is missing — it raises a blocking error;
- **never** derives a table it does not have (no monthly ÷ 4 for weekly);
- **never** applies a rule graded 🟡 or 🔴 in LIVE mode;
- **never** reclassifies an employee's employment type;
- **never** marks a statutory amount paid without a payment record;
- **always** records which rule version, exchange rate and strategy produced every figure;
- **always** says, in plain words, what is unverified and what must be done about it.

---

## APPENDIX A — SOURCES

All accessed 12 September 2026 **via search-index snippets only**. No source below was opened
directly; every official domain returned HTTP 403 from the network egress policy (§0.1).

**Attributed to official sources (obtained indirectly):**
ZIMRA PAYE system · ZIMRA PAYE Explained · ZIMRA tax tables (USD and ZWG 2025) · ZIMRA public
notice 24 of 2022 (declaration and payment of tax in foreign currency) · ZIMRA public notice 20 of
2023 (exchange rates for income tax purposes) · ZIMRA public notice 51 of 2023 (PAYE tables review)
· ZIMRA public notice 04 of 2025 (ITF16 submission) · ZIMRA FDS year-end procedures · ZIMRA tax
credits page · Income Tax Act [Chapter 23:06] · Labour Act [Chapter 28:01] s.12(3), s.12(3a) ·
SI 393 of 1993 (NSSA POBS) · SI 112/111 of 2021 (Construction Industry CBA) · Manpower Planning and
Development Act [Chapter 28:02] s.53, SI 74 and 392 of 1999 · Finance Act No. 2 of 2024 · NSSA
contributions, schemes and coverage pages.

**Professional and secondary sources:**
Lucent Consultancy (APWCS, NSSA POBS/APWCS, tax credits, remuneration and PAYE, NEC compliance,
ZIMDEF) · M&J Consultants (NSSA rates, PAYE/NSSA/ZIMDEF, SDF levy, 2026 tax guide, compliance
calendar) · Belina Payroll (NSSA POBS, NSSA calculation, bonus processing, ZIMDEF rebates, ZIMRA
returns) · Sage knowledge base and community (Zimbabwe tax summaries, NSSA insurable earnings,
bonus exemption updates) · PaySpace (bonus threshold revision) · Multiplier, AnooreHR, Workforce
Africa, Asanify, Playroll, Rivermate (payroll guides) · Mondaq, Business Times, Touchstone,
Misfort Tax (tax credits) · Chambers Global Practice Guides, Muvingi & Mugadza, Marume & Furidzo
(employment law) · KPMG TaxNewsFlash and CAA (2026 budget) · SSA Programs Throughout the World —
Africa (NSSA coverage) · registercompany.co.zw, zimtax.co.zw (tax tables, calendars, leave).

**Blocked and therefore unread:** zimra.co.zw · nssa.org.zw · veritaszim.net · zimlii.org ·
africanlii.org · rbz.co.zw · zimtreasury.gov.zw · parlzim.gov.zw · zimdef.org.zw ·
taxsummaries.pwc.com
