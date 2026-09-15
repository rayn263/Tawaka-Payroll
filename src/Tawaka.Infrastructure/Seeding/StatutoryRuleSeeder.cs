using Microsoft.EntityFrameworkCore;
using Tawaka.Domain.Currencies;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Statutory;
using Tawaka.Infrastructure.Persistence;

namespace Tawaka.Infrastructure.Seeding;

/// <summary>
/// Seeds reference data and the statutory rule baseline from
/// ZIMBABWE_PAYROLL_COMPLIANCE_SPEC_V1.md.
/// <para>
/// Every seeded rule carries its real verification status and source. Nothing here is marked
/// Verified, because no rule in the specification has been confirmed against a primary source —
/// the official domains are unreachable. These rules therefore calculate in Development mode and
/// are refused by the live payroll gate until a human verifies them (ADR-012).
/// </para>
/// </summary>
public sealed class StatutoryRuleSeeder
{
    private const string SpecReference = "ZIMBABWE_PAYROLL_COMPLIANCE_SPEC_V1.md";

    private readonly PayrollDbContext _context;

    public StatutoryRuleSeeder(PayrollDbContext context)
    {
        _context = context;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedCurrenciesAsync(cancellationToken).ConfigureAwait(false);
        await SeedSettingsAsync(cancellationToken).ConfigureAwait(false);
        await SeedRulesAsync(cancellationToken).ConfigureAwait(false);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SeedCurrenciesAsync(CancellationToken cancellationToken)
    {
        if (await _context.Currencies.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        _context.Currencies.AddRange(
            new Currency
            {
                Code = "USD",
                Name = "United States Dollar",
                DisplayCode = "USD",
                Symbol = "$",
                DecimalPlaces = 2,
                SortOrder = 1
            },
            new Currency
            {
                Code = "ZWG",
                Name = "Zimbabwe Gold",
                DisplayCode = "ZiG",
                Symbol = "ZiG",
                DecimalPlaces = 2,
                SortOrder = 2
            });
    }

    private async Task SeedSettingsAsync(CancellationToken cancellationToken)
    {
        if (await _context.AppSettings.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        _context.AppSettings.AddRange(
            Setting(SettingKeys.PayrollMode, nameof(PayrollMode.Development), "string",
                SettingCategory.Payroll,
                "Development or Live. Live payroll resolves only Verified statutory rules."),
            Setting(SettingKeys.DefaultCurrency, "USD", "string", SettingCategory.Currency,
                "Default payroll currency for new employees."),
            Setting(SettingKeys.ReportingCurrency, "USD", "string", SettingCategory.Currency,
                "Default currency for consolidated reporting. Never used to merge currency totals."),
            Setting(SettingKeys.AllowMixedCurrencyPayroll, "false", "bool", SettingCategory.Currency,
                "Whether one employee may be paid in more than one currency. Requires an approved " +
                "multi-currency tax strategy."),
            Setting(SettingKeys.EnforceSegregationOfDuties, "true", "bool", SettingCategory.Security,
                "When true, the user who calculates a payroll run cannot approve it."),
            Setting(SettingKeys.CompanyName, string.Empty, "string", SettingCategory.Company,
                "Registered company name shown on payslips and reports."));
    }

    private static AppSetting Setting(
        string key, string value, string dataType, SettingCategory category, string description) =>
        new()
        {
            Key = key,
            Value = value,
            DataType = dataType,
            Category = category,
            Description = description
        };

    private async Task SeedRulesAsync(CancellationToken cancellationToken)
    {
        if (await _context.StatutoryRules.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var from = new DateOnly(2026, 1, 1);
        var to = new DateOnly(2026, 12, 31);

        // ---- PAYE: USD monthly -------------------------------------------------------------
        // Bands are corroborated across sources; the official fixed-deduction ("less") column was
        // NOT obtained, so FixedDeduction is left null and the rule stays Unverified (spec Q26).
        var usdMonthly = new TaxRule
        {
            RuleId = "PAYE-USD-2026-MONTHLY",
            Name = "PAYE USD monthly table 2026",
            Currency = "USD",
            TaxYear = 2026,
            PeriodBasis = PeriodBasis.Monthly,
            CalculationMethod = CalculationMethod.PeriodTable,
            EffectiveFrom = from,
            EffectiveTo = to,
            VerificationStatus = VerificationStatus.Unverified,
            Notes = "SEED DATA. Bands corroborated by multiple secondary sources. The official " +
                    "fixed-deduction column has not been obtained (spec Q26), so this table cannot " +
                    "reconcile to ZIMRA's published figures and must not be used for live payroll.",
            Source = new RuleSource
            {
                Source = "Secondary sources; ZIMRA USD monthly tax table not read directly",
                SourceReference = SpecReference + " §1.3"
            }
        };
        AddBrackets(usdMonthly,
            (1, 0m, 100m, 0m),
            (2, 100m, 300m, 0.20m),
            (3, 300m, 3000m, 0.25m),
            (4, 3000m, null, 0.40m));
        _context.TaxRules.Add(usdMonthly);

        // ---- PAYE: ZiG annual --------------------------------------------------------------
        var zwgAnnual = new TaxRule
        {
            RuleId = "PAYE-ZWG-2026-ANNUAL",
            Name = "PAYE ZiG annual table 2026",
            Currency = "ZWG",
            TaxYear = 2026,
            PeriodBasis = PeriodBasis.Annual,
            CalculationMethod = CalculationMethod.PeriodTable,
            EffectiveFrom = from,
            EffectiveTo = to,
            VerificationStatus = VerificationStatus.Unverified,
            Notes = "SEED DATA. Weaker corroboration than the USD table, and the fixed-deduction " +
                    "column is unknown. ZiG bands are set independently and are never derived from " +
                    "the USD table.",
            Source = new RuleSource
            {
                Source = "Secondary sources; ZIMRA ZWG tax table not read directly",
                SourceReference = SpecReference + " §1.4"
            }
        };
        AddBrackets(zwgAnnual,
            (1, 0m, 33600m, 0m),
            (2, 33600m, 100800m, 0.20m),
            (3, 100800m, 1008000m, 0.25m),
            (4, 1008000m, null, 0.40m));
        _context.TaxRules.Add(zwgAnnual);

        // ---- AIDS Levy ---------------------------------------------------------------------
        // Best-evidenced rule in the set: wording attributed to ZIMRA states the levy is charged
        // on tax after credits. Still Supported, not Verified: the page was not opened.
        _context.AidsLevyRules.Add(new AidsLevyRule
        {
            RuleId = "AIDS-LEVY-2026",
            Name = "AIDS Levy 3% of tax after credits",
            Currency = null,
            CalculationMethod = CalculationMethod.PercentageOfBase,
            Rate = 0.03m,
            Base = AidsLevyBase.TaxAfterCredits,
            EffectiveFrom = from,
            EffectiveTo = null,
            VerificationStatus = VerificationStatus.Supported,
            Notes = "Resolves compliance question Q2. ZIMRA-attributed wording: tax credits are " +
                    "deducted, then 3% is calculated and added to the tax after credits.",
            Source = new RuleSource
            {
                Source = "ZIMRA PAYE guidance (obtained indirectly via search index)",
                SourceReference = SpecReference + " §4"
            }
        });

        // ---- NSSA POBS ---------------------------------------------------------------------
        _context.NssaRules.Add(new NssaRule
        {
            RuleId = "NSSA-POBS-2026-USD",
            Name = "NSSA Pension and Other Benefits Scheme 2026 (USD)",
            Currency = "USD",
            CalculationMethod = CalculationMethod.CappedPercentage,
            EmployeeRate = 0.045m,
            EmployerRate = 0.045m,
            CeilingAmount = 700m,
            CeilingPeriodBasis = PeriodBasis.Monthly,
            // Deliberately NotDetermined: how the monthly ceiling applies to weekly payroll is
            // unresolved (spec Q22) and the three candidate methods differ roughly fourfold.
            CeilingApplication = CeilingApplicationMethod.NotDetermined,
            EarningsBasis = NssaEarningsBasis.BasicOnly,
            GrossUpEnabled = false,
            GrossUpTriggerMultiplier = null,
            MinimumAge = 16,
            MaximumAge = 64,
            MinimumDaysInMonth = 18,
            EffectiveFrom = from,
            EffectiveTo = null,
            VerificationStatus = VerificationStatus.Supported,
            Notes = "SEED DATA. 4.5% each side on insurable earnings capped at USD 700/month. " +
                    "Basic salary only; overtime and bonuses excluded. The SI 393/93 s.12 gross-up " +
                    "is disabled because sources give two different trigger thresholds (Q4a). The " +
                    "ceiling is reportedly gazetted quarterly, so this rule needs regular review.",
            Source = new RuleSource
            {
                Source = "NSSA contributions guidance and SI 393 of 1993 (obtained indirectly)",
                SourceReference = SpecReference + " §5"
            }
        });

        SeedNssaEligibility(from);
        SeedOvertimeAndDivisor(from, to);

        // ---- Employer levies ---------------------------------------------------------------
        _context.EmployerLevyRules.Add(new EmployerLevyRule
        {
            RuleId = "ZIMDEF-2026",
            Name = "ZIMDEF manpower development levy 1%",
            Currency = null,
            CalculationMethod = CalculationMethod.PercentageOfBase,
            LevyType = EmployerLevyType.Zimdef,
            Base = LevyBase.LeviableWageBill,
            EmployerPortionRate = 0.01m,
            EmployeePortionRate = 0m,
            DueDayOfFollowingMonth = 15,
            EffectiveFrom = from,
            EffectiveTo = null,
            VerificationStatus = VerificationStatus.Supported,
            Notes = "Statutory basis cited as Manpower Planning and Development Act [Cap 28:02] " +
                    "s.53 and SI 74 and 392 of 1999. Base is wider than the PAYE base: it includes " +
                    "employer NSSA and pension contributions. Deadline is the 15th, not the 10th.",
            Source = new RuleSource
            {
                Source = "ZIMDEF; Manpower Planning and Development Act [Cap 28:02] s.53",
                SourceReference = SpecReference + " §7"
            }
        });

        _context.EmployerLevyRules.Add(new EmployerLevyRule
        {
            RuleId = "SDF-2026",
            Name = "Standards Development Fund levy 0.5%",
            Currency = null,
            CalculationMethod = CalculationMethod.PercentageOfBase,
            LevyType = EmployerLevyType.StandardsDevelopmentFund,
            Base = LevyBase.GrossWageBill,
            EmployerPortionRate = 0.005m,
            EmployeePortionRate = 0m,
            DueDayOfFollowingMonth = 10,
            EffectiveFrom = from,
            EffectiveTo = null,
            IsActive = false,
            VerificationStatus = VerificationStatus.Unverified,
            Notes = "SEEDED INACTIVE. The 0.5% rate has no statutory anchor in the evidence found, " +
                    "and liability and exemptions are not established. Activating this levy is a " +
                    "deliberate act after confirming the company is liable.",
            Source = new RuleSource
            {
                Source = "Professional sources only; no Act or SI reference obtained",
                SourceReference = SpecReference + " §8"
            }
        });

        // ---- Tax credits and exemptions ----------------------------------------------------
        foreach (var (creditType, ruleId) in new[]
                 {
                     (TaxCreditType.Elderly, "CREDIT-ELDERLY-2026"),
                     (TaxCreditType.Blind, "CREDIT-BLIND-2026"),
                     (TaxCreditType.Disabled, "CREDIT-DISABLED-2026")
                 })
        {
            _context.TaxCreditRules.Add(new TaxCreditRule
            {
                RuleId = ruleId,
                Name = $"{creditType} persons' tax credit 2026",
                Currency = "USD",
                CalculationMethod = CalculationMethod.FlatAmount,
                CreditType = creditType,
                Amount = 75m,
                AmountPeriodBasis = PeriodBasis.Monthly,
                AnnualCap = 900m,
                EffectiveFrom = from,
                EffectiveTo = null,
                VerificationStatus = VerificationStatus.Unverified,
                Notes = "SEED DATA. USD 75/month (USD 900/year). Credits are capped at the tax " +
                        "chargeable and are never refunded.",
                Source = new RuleSource
                {
                    Source = "Professional sources; ZIMRA credits page not read directly",
                    SourceReference = SpecReference + " §18"
                }
            });
        }

        _context.TaxCreditRules.Add(new TaxCreditRule
        {
            RuleId = "CREDIT-MEDICAL-AID-2026",
            Name = "Medical aid contribution credit 2026",
            Currency = "USD",
            CalculationMethod = CalculationMethod.PercentageOfBase,
            CreditType = TaxCreditType.MedicalAidContribution,
            Amount = 0m,
            PercentageOfQualifyingAmount = null,
            AmountPeriodBasis = PeriodBasis.Monthly,
            EffectiveFrom = from,
            EffectiveTo = null,
            IsActive = false,
            VerificationStatus = VerificationStatus.Unverified,
            Notes = "SEEDED INACTIVE WITH NO PERCENTAGE. Sources conflict directly: one states 50% " +
                    "of medical aid contributions, another 100% (spec Q24). Seeding either would " +
                    "be a guess that halves or doubles every affected employee's credit.",
            Source = new RuleSource
            {
                Source = "Conflicting professional sources",
                SourceReference = SpecReference + " §18"
            }
        });

        _context.TaxExemptionRules.Add(new TaxExemptionRule
        {
            RuleId = "EXEMPT-BONUS-2026-USD",
            Name = "Annual bonus exemption 2026 (USD)",
            Currency = "USD",
            CalculationMethod = CalculationMethod.FlatAmount,
            ExemptionType = TaxExemptionType.AnnualBonus,
            LimitAmount = 700m,
            LimitPeriodBasis = PeriodBasis.Annual,
            EffectiveFrom = from,
            EffectiveTo = null,
            VerificationStatus = VerificationStatus.Supported,
            Notes = "Raised from USD 400 to USD 700 by Finance Act No. 2 of 2024. Whether the " +
                    "exemption covers any bonus or only an annual/13th-cheque bonus is unresolved " +
                    "(Q28), so it attaches to a specific earning type rather than anything named " +
                    "'bonus'.",
            Source = new RuleSource
            {
                Source = "Finance Act No. 2 of 2024 (obtained indirectly)",
                SourceReference = SpecReference + " §15"
            }
        });

        // ---- Multi-currency tax strategy ---------------------------------------------------
        _context.CurrencyTaxStrategyRules.Add(new CurrencyTaxStrategyRule
        {
            RuleId = "CURRENCY-STRATEGY-2026",
            Name = "Multi-currency PAYE strategy 2026",
            Currency = null,
            CalculationMethod = CalculationMethod.Strategy,
            Strategy = CurrencyTaxStrategy.AggregateInPrimaryCurrency,
            PrimaryCurrency = "USD",
            RateDetermination = RateDeterminationRule.NotDetermined,
            RateType = RateType.Interbank,
            ApprovedByAdvisor = false,
            EffectiveFrom = from,
            EffectiveTo = null,
            VerificationStatus = VerificationStatus.Unverified,
            Notes = "The recommended interpretation, not a confirmed one (spec §2.4). Requires " +
                    "advisor sign-off AND a rate determination rule before it can be used. " +
                    "Employees paid in a single currency are unaffected.",
            Source = new RuleSource
            {
                Source = "ZIMRA-attributed aggregation guidance, which references RTGS$ and may " +
                         "predate ZiG",
                SourceReference = SpecReference + " §2"
            }
        });
    }

    /// <summary>
    /// Overtime categories and the pay divisor.
    /// <para>
    /// The compliance specification §14 records that the overtime <em>rate</em> is a contractual
    /// and NEC matter rather than a national statutory rate, while the statutory <em>treatment</em>
    /// — taxable, excluded from NSSA insurable earnings — is evidenced. So these are seeded with
    /// the treatment from §14 and with the multipliers most commonly cited, all graded Unverified:
    /// an employer verifies them against their own contracts or collective bargaining agreement,
    /// which is a thing they can actually do, unlike reading a blocked ZIMRA page.
    /// </para>
    /// </summary>
    private void SeedOvertimeAndDivisor(DateOnly from, DateOnly to)
    {
        var categories = new[]
        {
            ("OT_WEEKDAY", "Overtime (ordinary day)", 1.5m),
            ("OT_SUNDAY", "Overtime (Sunday)", 2.0m),
            ("OT_PUBLIC_HOLIDAY", "Overtime (public holiday)", 2.0m)
        };

        foreach (var (code, name, multiplier) in categories)
        {
            _context.StatutoryRules.Add(new OvertimeRule
            {
                RuleId = $"OVERTIME-2026-{code}",
                Name = name,
                CategoryCode = code,
                CategoryName = name,
                Multiplier = multiplier,
                CalculationMethod = CalculationMethod.Multiplier,
                EffectiveFrom = from,
                EffectiveTo = to,

                // §14: overtime is fully taxable but excluded from NSSA insurable earnings. That
                // exclusion is the operationally significant one — applying NSSA to gross
                // over-deducts from exactly the site staff who work the most overtime.
                IsTaxable = true,
                IsNssaApplicable = false,
                VerificationStatus = VerificationStatus.Unverified,
                Notes = "SEED DATA. The multiplier is the commonly cited rate, not a verified one. " +
                        "Where a NEC collective bargaining agreement applies it is a Statutory " +
                        "Instrument and governs (spec Q23, Q31). Confirm against the contract of " +
                        "employment or the applicable CBA before using in live payroll.",
                Source = new RuleSource
                {
                    Source = "Commonly cited overtime rates; contractual or NEC in origin",
                    SourceReference = SpecReference + " §14"
                }
            });
        }

        // The divisor that converts a monthly salary into a daily or hourly rate. Nothing about
        // this is arithmetic: 22 working days is a convention, and which convention applies
        // changes what an employee loses for a day of unpaid leave (spec Q33).
        _context.StatutoryRules.Add(new PayDivisorRule
        {
            RuleId = "PAY-DIVISOR-2026-MONTHLY",
            Name = "Monthly salary to daily and hourly rate",
            SalaryBasis = PeriodBasis.Monthly,
            DaysInPeriod = 22m,
            HoursInPeriod = 176m,
            CalculationMethod = CalculationMethod.Divisor,
            EffectiveFrom = from,
            EffectiveTo = to,
            VerificationStatus = VerificationStatus.Unverified,
            Notes = "SEED DATA. 22 working days and 176 ordinary hours is a common convention, " +
                    "not an established one. The Labour Act and the applicable NEC agreement " +
                    "govern (spec Q33). Until verified, overtime for salaried staff and unpaid " +
                    "leave deductions calculate in development mode only.",
            Source = new RuleSource
            {
                Source = "Common payroll convention; no authoritative source obtained",
                SourceReference = SpecReference + " §14, Q33"
            }
        });
    }

    private void SeedNssaEligibility(DateOnly from)
    {
        // Types named in the coverage evidence are Supported; the rest are inferred and therefore
        // Unverified, which blocks live payroll for those employees only.
        var eligibility = new (string Code, bool Eligible, VerificationStatus Status, string Note)[]
        {
            ("Permanent", true, VerificationStatus.Supported, "Compulsory coverage, ages 16 to under 65."),
            ("Contract", true, VerificationStatus.Supported, "Contract employment named in the coverage rules."),
            ("Temporary", true, VerificationStatus.Supported, "Temporary employment named in the coverage rules."),
            ("Seasonal", true, VerificationStatus.Supported, "Seasonal employment named in the coverage rules."),
            ("Casual", true, VerificationStatus.Supported,
                "Contributes only where engaged for 18 days or more in the month."),
            ("ProjectBased", true, VerificationStatus.Unverified, "Inferred from general coverage; not named in the evidence."),
            ("PartTime", true, VerificationStatus.Unverified, "Inferred; ceiling treatment for part-timers unclear."),
            ("Occasional", true, VerificationStatus.Unverified, "Inferred; treat as casual pending verification."),
            ("Intern", true, VerificationStatus.Unverified, "Inferred; covered if aged 16 or over."),
            ("CommissionBased", true, VerificationStatus.Unverified, "Inferred; commission's status in insurable earnings unclear."),
            ("HourlyPaid", true, VerificationStatus.Unverified, "Inferred; ceiling apportionment unresolved."),
            ("Domestic", false, VerificationStatus.Supported, "Domestic employees are excluded from the scheme.")
        };

        foreach (var (code, eligible, status, note) in eligibility)
        {
            _context.NssaEligibilityRules.Add(new NssaEligibilityRule
            {
                RuleId = $"NSSA-ELIG-{code.ToUpperInvariant()}-2026",
                Name = $"NSSA eligibility — {code}",
                Currency = null,
                CalculationMethod = CalculationMethod.FlatAmount,
                EmploymentTypeCode = code,
                IsEligible = eligible,
                EffectiveFrom = from,
                EffectiveTo = null,
                VerificationStatus = status,
                Notes = note,
                Source = new RuleSource
                {
                    Source = "NSSA coverage guidance and SI 393 of 1993 (obtained indirectly)",
                    SourceReference = SpecReference + " §5.3"
                }
            });
        }
    }

    private static void AddBrackets(
        TaxRule rule, params (int Sequence, decimal Lower, decimal? Upper, decimal Rate)[] bands)
    {
        foreach (var (sequence, lower, upper, rate) in bands)
        {
            rule.Brackets.Add(new TaxBracket
            {
                Sequence = sequence,
                LowerBound = lower,
                UpperBound = upper,
                Rate = rate,
                FixedDeduction = null
            });
        }
    }
}
