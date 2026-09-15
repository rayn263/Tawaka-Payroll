using Tawaka.Domain.Common;
using Tawaka.Domain.Statutory;
using Tawaka.Payroll.Engine.Inputs;
using Tawaka.Payroll.Engine.Results;
using Tawaka.Payroll.Engine.Rounding;
using Tawaka.Payroll.Engine.Stages;
using Tawaka.Payroll.Engine.Tracing;

namespace Tawaka.Payroll.Engine;

/// <summary>
/// The payroll calculation engine.
/// <para>
/// Pure: it reads nothing but the snapshot it is given — no database, no clock, no current user —
/// and returns a result plus a full trace. The same snapshot always produces the same result,
/// which is what makes payroll reproducible and disputes answerable (ADR-005).
/// </para>
/// <para>
/// No statutory value appears in this file. Every rate, threshold and ceiling arrives through the
/// resolved rules on the snapshot, and where a required rule is missing or unverified the engine
/// leaves the figure unresolved rather than substituting zero.
/// </para>
/// </summary>
public sealed class PayrollCalculator
{
    public const string Version = "1.0.0";

    public PayrollResult Calculate(PayrollInputSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var context = new CalculationContext(snapshot);

        // The ordered pipeline. Each stage appends to the trace and may record an unresolved item.
        DeriveApprovedInputs(context);
        GatherEarnings(context);
        ApplyExemptions(context);
        ComputeGross(context);
        ComputeNssa(context);
        ComputePreTaxDeductions(context);
        DetermineTaxBase(context);
        ComputePaye(context);
        ApplyTaxCredits(context);
        ComputeAidsLevy(context);
        ComputePostTaxDeductions(context);
        ComputeNetPay(context);
        ComputeEmployerCosts(context);
        AllocateCost(context);

        return context.Build();
    }

    // ---- Stages ----------------------------------------------------------------------------

    /// <summary>
    /// Turns approved time, absence and loan inputs into earnings and deductions before anything
    /// else runs, so the rest of the pipeline sees one uniform set of lines and does not need to
    /// know where any of them came from.
    /// </summary>
    private static void DeriveApprovedInputs(CalculationContext context)
    {
        var derived = new List<Inputs.EarningInput>();
        derived.AddRange(TimeAndAbsenceCalculator.DeriveTimeBasedPay(
            context.Snapshot, context.Unresolve, context.Trace));
        derived.AddRange(TimeAndAbsenceCalculator.DeriveOvertime(
            context.Snapshot, context.Unresolve, context.Trace));

        var derivedDeductions = new List<Inputs.DeductionInput>();
        derivedDeductions.AddRange(TimeAndAbsenceCalculator.DeriveUnpaidLeave(
            context.Snapshot, context.Unresolve, context.Trace));
        derivedDeductions.AddRange(TimeAndAbsenceCalculator.DeriveLoanDeductions(
            context.Snapshot, context.Trace));

        context.AddDerivedInputs(derived, derivedDeductions);
        WarnOnCasualEngagement(context);
    }

    /// <summary>
    /// Warns when a casual worker's engagement approaches or passes the point at which the Labour
    /// Act deems them permanent.
    /// <para>
    /// It warns and does nothing else. It does not change the employment type, start leave
    /// accrual or alter NSSA treatment: the legal deeming happens whether the software notices or
    /// not, and a silent reclassification would be this system making a legal determination it has
    /// no standing to make. Suppressing the warning would leave a real liability invisible, which
    /// is why it is not optional either (spec §11).
    /// </para>
    /// </summary>
    private static void WarnOnCasualEngagement(CalculationContext context)
    {
        if (context.Snapshot.CasualEngagement is not { IsApproaching: true } engagement)
        {
            return;
        }

        var message = engagement.HasPassed
            ? $"Casual engagement threshold PASSED — {engagement.Describe()} of engagement in the " +
              $"four months to {engagement.WindowEnd:dd MMM yyyy}. Labour Act s.12(3) deems this " +
              "employee to be on a contract without limit of time. This has contractual, leave and " +
              "NSSA consequences. Seek advice and update the employment record. The system has " +
              "not changed anything."
            : $"Casual engagement threshold approaching — {engagement.Describe()} of engagement in " +
              $"the four months to {engagement.WindowEnd:dd MMM yyyy}. At " +
              $"{engagement.DeemedAtDays} days, Labour Act s.12(3) deems the employee to be on a " +
              "contract without limit of time. Seek advice and update the employment record if " +
              "appropriate.";

        context.Warn(new CalculationWarning(
            UnresolvedCodes.CasualEngagementThreshold, context.Snapshot.EmploymentTypeCode, message));
    }

    private static void GatherEarnings(CalculationContext context)
    {
        foreach (var earning in context.Snapshot.Earnings)
        {
            if (earning.Amount.Currency != context.Currency)
            {
                // A component in another currency needs the multi-currency strategy, which is a
                // dated, advisor-approved rule — never an assumption made here.
                var strategy = context.Snapshot.Rules.CurrencyStrategy;
                if (strategy is null || strategy.Strategy == CurrencyTaxStrategy.SingleCurrency)
                {
                    context.Unresolve(new UnresolvedItem(
                        UnresolvedCodes.CurrencyStrategyUnresolved, earning.Code,
                        $"'{earning.Name}' is denominated in {earning.Amount.Currency} but this " +
                        $"employee's payroll currency is {context.Currency}, and no approved " +
                        "multi-currency tax strategy is configured.",
                        StatutoryRuleType.CurrencyTaxStrategy, strategy?.RuleId,
                        strategy?.VerificationStatus, "Q1",
                        "Resolve compliance question Q1 and record advisor approval of the " +
                        "multi-currency strategy."));
                    continue;
                }
            }

            if (!earning.TreatmentVerification.IsUsableInLivePayroll() &&
                context.Snapshot.Mode == Domain.Payroll.PayrollMode.Live)
            {
                context.Unresolve(new UnresolvedItem(
                    UnresolvedCodes.EarningTreatmentUnverified, earning.Code,
                    $"The statutory treatment of '{earning.Name}' has not been verified.",
                    null, null, earning.TreatmentVerification, null,
                    "Confirm whether this earning is taxable and NSSA-applicable, record the " +
                    "source, and set its treatment to Verified."));
            }

            // Round each line to currency precision as it enters. An earning is a cash amount, and
            // if lines carry more precision than the totals the payslip will not reconcile to the
            // lines that make it up.
            var rounded = context.Round(earning.Amount);
            if (rounded != earning.Amount)
            {
                context.Trace(new TraceEntryBuilder("GatherEarnings", earning.Code)
                    .Input("Amount as captured", earning.Amount)
                    .Rounded(rounded, context.Rounding.Describe())
                    .Explain("Rounded to currency precision on entry, so lines and totals agree.")
                    .Build());
            }

            context.AddEarning(earning with { Amount = rounded });
        }
    }

    /// <summary>
    /// Applies exemption limits, such as the annual bonus exemption, splitting each line into its
    /// exempt and taxable portions rather than discarding either.
    /// </summary>
    private static void ApplyExemptions(CalculationContext context)
    {
        var exemption = context.Snapshot.Rules.BonusExemption;

        foreach (var line in context.Earnings.Where(l => l.Input.IsExemptUpToLimit).ToList())
        {
            if (exemption is null)
            {
                context.Unresolve(new UnresolvedItem(
                    UnresolvedCodes.ExemptionRuleUnresolved, line.Input.Code,
                    $"'{line.Input.Name}' is flagged exempt up to a limit, but no exemption rule " +
                    "is available.",
                    StatutoryRuleType.TaxExemption, null, null, "Q28",
                    "Configure the exemption limit and its effective dates."));
                continue;
            }

            var limit = new Money(exemption.LimitAmount, context.Currency);
            var used = context.Snapshot.BonusExemptionUsedYearToDate ?? Money.Zero(context.Currency);
            var remaining = limit - used;
            if (remaining.IsNegative)
            {
                remaining = Money.Zero(context.Currency);
            }

            var exempt = Money.Min(line.Input.Amount, remaining);
            var taxable = line.Input.Amount - exempt;

            line.ExemptAmount = exempt;
            line.TaxableAmount = taxable;

            context.Trace(new TraceEntryBuilder("ApplyExemptions", line.Input.Code)
                .FromRule(exemption)
                .Input("Amount", line.Input.Amount)
                .Input("Annual limit", limit)
                .Input("Already used this tax year", used)
                .Step("Exempt portion", exempt.Amount)
                .Step("Taxable portion", taxable.Amount)
                .Result(taxable)
                .Explain("The exemption is tracked across the tax year so it cannot be claimed twice.")
                .Build());
        }
    }

    private static void ComputeGross(CalculationContext context)
    {
        // Where a component of pay could not be produced, the gross is not known. Totalling only
        // the parts that worked would report a figure that looks complete and is not.
        if (context.HasUnresolvedEarnings)
        {
            context.GrossEarnings = null;
            context.Trace(new TraceEntryBuilder("ComputeGross", "Gross earnings")
                .Explain("Not determined: one or more components of pay could not be calculated. " +
                         "The unresolved items name each one. This is an absent figure, not zero.")
                .Build());
            return;
        }

        var gross = Money.Sum(context.Currency,
            context.Earnings.Where(l => l.Input.IsIncludedInGross &&
                                        l.Input.Amount.Currency == context.Currency)
                .Select(l => l.Input.Amount));

        // Lines are already at currency precision, so the sum needs no further rounding —
        // re-rounding a sum of rounded values can only create a discrepancy.
        context.GrossEarnings = gross;

        var trace = new TraceEntryBuilder("ComputeGross", "Gross earnings");
        foreach (var line in context.Earnings.Where(l => l.Input.IsIncludedInGross))
        {
            trace.Step(line.Input.Name, line.Input.Amount.Amount);
        }

        var excluded = context.Earnings.Where(l => !l.Input.IsIncludedInGross).ToList();
        if (excluded.Count > 0)
        {
            trace.Input("Excluded from gross",
                string.Join(", ", excluded.Select(l => $"{l.Input.Name} (reimbursement)")));
        }

        context.Trace(trace.Result(context.GrossEarnings.Value)
            .Explain("Gross earnings are the earnings flagged as included in gross pay.")
            .Build());
    }

    private static void ComputeNssa(CalculationContext context)
    {
        var calculator = new NssaCalculator(context.Rounding);
        var computation = calculator.Compute(context.Snapshot);

        foreach (var entry in computation.Trace)
        {
            context.Trace(entry);
        }

        foreach (var warning in computation.Warnings)
        {
            context.Warn(warning);
        }

        if (computation.Unresolved is not null)
        {
            context.Unresolve(computation.Unresolved);
            return;
        }

        context.NssaInsurableEarnings = computation.InsurableEarnings;
        context.NssaEmployee = computation.EmployeeContribution;
        context.NssaEmployer = computation.EmployerContribution;

        if (computation.EmployeeContribution is { } employee)
        {
            context.AddStatutoryDeduction("NSSA_EE", "NSSA Employee (POBS)", employee,
                reducesTaxableIncome: true);
        }
    }

    private static void ComputePreTaxDeductions(CalculationContext context)
    {
        foreach (var deduction in context.Snapshot.Deductions.Where(d => d.AppliesBeforeTax))
        {
            context.AddDeduction(deduction with { Amount = context.Round(deduction.Amount) });
        }
    }

    /// <summary>
    /// Determines the amount the tax table is applied to: taxable earnings less the deductions
    /// that reduce taxable income.
    /// </summary>
    private static void DetermineTaxBase(CalculationContext context)
    {
        var taxableEarnings = Money.Sum(context.Currency,
            context.Earnings
                .Where(l => l.Input.IsTaxable && l.Input.Amount.Currency == context.Currency)
                .Select(l => l.TaxableAmount ?? l.Input.Amount));

        var allowable = Money.Sum(context.Currency,
            context.Deductions.Where(d => d.ReducesTaxableIncome).Select(d => d.Amount));

        var taxable = taxableEarnings - allowable;
        if (taxable.IsNegative)
        {
            taxable = Money.Zero(context.Currency);
        }

        context.TaxableIncome = context.Round(taxable);

        var trace = new TraceEntryBuilder("DetermineTaxBase", "Taxable income")
            .Input("Taxable earnings", taxableEarnings);
        foreach (var deduction in context.Deductions.Where(d => d.ReducesTaxableIncome))
        {
            trace.Step($"less {deduction.Name}", -deduction.Amount.Amount);
        }

        context.Trace(trace.Result(context.TaxableIncome.Value)
            .Explain("Taxable income is taxable earnings less the deductions that are allowable " +
                     "against it, such as the NSSA employee contribution.")
            .Build());
    }

    private static void ComputePaye(CalculationContext context)
    {
        if (context.Snapshot.StatutoryProfile.IsPayeExempt)
        {
            context.PayeBeforeCredits = Money.Zero(context.Currency);
            context.Trace(new TraceEntryBuilder("ComputePaye", "PAYE")
                .Explain("Employee is recorded as exempt from PAYE.")
                .Result(context.PayeBeforeCredits.Value)
                .Build());
            return;
        }

        var table = context.Snapshot.Rules.PayeTable;
        if (table is null)
        {
            context.Unresolve(context.Snapshot.Rules.FindUnresolved(StatutoryRuleType.PayeTable) ??
                new UnresolvedItem(UnresolvedCodes.PayeTableUnresolved, "PAYE",
                    $"No PAYE table is available for {context.Currency} " +
                    $"{context.Snapshot.PeriodBasis.ToString().ToLowerInvariant()} pay on " +
                    $"{context.Snapshot.PayDate:dd MMM yyyy}.",
                    StatutoryRuleType.PayeTable, null, null, "Q26",
                    "Load the official table for this currency and pay frequency. Tables are " +
                    "never derived from another period basis."));
            return;
        }

        if (context.TaxableIncome is not { } taxable)
        {
            return;
        }

        var computation = new PayeTableCalculator(context.Rounding).Compute(table, taxable);
        context.Trace(computation.Trace);

        if (computation.Unresolved is not null)
        {
            context.Unresolve(computation.Unresolved);
            return;
        }

        context.PayeBeforeCredits = computation.Tax;
    }

    private static void ApplyTaxCredits(CalculationContext context)
    {
        if (context.PayeBeforeCredits is not { } paye)
        {
            return;
        }

        var profile = context.Snapshot.StatutoryProfile;
        var entitlements = new List<(TaxCreditType Type, bool Entitled)>
        {
            (TaxCreditType.Elderly, profile.IsElderlyCreditEligible),
            (TaxCreditType.Disabled, profile.IsDisabledCreditEligible),
            (TaxCreditType.Blind, profile.IsBlindCreditEligible)
        };

        var credits = Money.Zero(context.Currency);
        var trace = new TraceEntryBuilder("ApplyTaxCredits", "Tax credits")
            .Input("Tax before credits", paye);

        foreach (var (type, entitled) in entitlements.Where(e => e.Entitled))
        {
            var rule = context.Snapshot.Rules.TaxCredits.FirstOrDefault(c => c.CreditType == type);
            if (rule is null)
            {
                context.Unresolve(new UnresolvedItem(
                    UnresolvedCodes.TaxCreditRuleUnresolved, "Tax credits",
                    $"The employee is entitled to the {type} credit, but no rule for it is available.",
                    StatutoryRuleType.TaxCredit, null, null, "Q8",
                    "Configure the credit amount and its effective dates."));
                return;
            }

            var amount = new Money(rule.Amount, context.Currency);
            credits += amount;
            trace.FromRule(rule).Step($"{type} credit", amount.Amount);
        }

        // Credits cannot reduce tax below zero, and excess is never refunded.
        if (credits > paye)
        {
            trace.Step($"Capped at tax chargeable ({paye.Amount:N2}); excess is not refundable",
                paye.Amount);
            credits = paye;
        }

        context.TaxCredits = context.Round(credits);
        context.PayeAfterCredits = context.Round(paye - context.TaxCredits.Value);

        context.Trace(trace.Result(context.TaxCredits.Value)
            .Explain("Credits are limited to the tax chargeable; no refund arises where they exceed it.")
            .Build());

        if (context.PayeAfterCredits is { } after)
        {
            context.AddStatutoryDeduction("PAYE", "PAYE", after, reducesTaxableIncome: false);
        }
    }

    private static void ComputeAidsLevy(CalculationContext context)
    {
        if (context.PayeAfterCredits is not { } after)
        {
            return;
        }

        var rule = context.Snapshot.Rules.AidsLevy;
        if (rule is null)
        {
            context.Unresolve(context.Snapshot.Rules.FindUnresolved(StatutoryRuleType.AidsLevy) ??
                new UnresolvedItem(UnresolvedCodes.AidsLevyRuleUnresolved, "AIDS Levy",
                    "No AIDS Levy rule is available for this pay date.",
                    StatutoryRuleType.AidsLevy, null, null, "Q2",
                    "Configure the AIDS Levy rate and its base."));
            return;
        }

        // The base is a rule, not an assumption: before or after credits is compliance question Q2.
        var basis = rule.Base == AidsLevyBase.TaxAfterCredits
            ? after
            : context.PayeBeforeCredits ?? after;

        var raw = basis.Amount * rule.Rate;
        var levy = new Money(context.Rounding.Round(raw), context.Currency);
        context.AidsLevy = levy;

        context.Trace(new TraceEntryBuilder("ComputeAidsLevy", "AIDS Levy")
            .FromRule(rule)
            .Input($"Tax {(rule.Base == AidsLevyBase.TaxAfterCredits ? "after" : "before")} credits", basis)
            .Step($"{basis.Amount:N2} x {rule.Rate * 100m:0.##}%", raw)
            .Raw(raw)
            .Rounded(levy, context.Rounding.Describe())
            .Explain($"Charged on income tax {(rule.Base == AidsLevyBase.TaxAfterCredits ? "after" : "before")} " +
                     "the deduction of credits, per the configured rule.")
            .Build());

        context.AddStatutoryDeduction("AIDSLEVY", "AIDS Levy", levy, reducesTaxableIncome: false);
    }

    private static void ComputePostTaxDeductions(CalculationContext context)
    {
        foreach (var deduction in context.Snapshot.Deductions
                     .Where(d => !d.AppliesBeforeTax)
                     .OrderBy(d => d.Priority))
        {
            context.AddDeduction(deduction with { Amount = context.Round(deduction.Amount) });
        }
    }

    private static void ComputeNetPay(CalculationContext context)
    {
        if (context.GrossEarnings is not { } gross || context.HasUnresolved)
        {
            return;
        }

        var statutory = Money.Sum(context.Currency,
            context.Deductions.Where(d => d.IsStatutory).Select(d => d.Amount));
        var other = Money.Sum(context.Currency,
            context.Deductions.Where(d => !d.IsStatutory).Select(d => d.Amount));
        var total = statutory + other;

        context.TotalStatutoryDeductions = statutory;
        context.TotalOtherDeductions = other;
        context.TotalDeductions = total;
        context.NetPay = gross - total;

        var trace = new TraceEntryBuilder("ComputeNetPay", "Net pay")
            .Input("Gross earnings", gross);
        foreach (var deduction in context.Deductions)
        {
            trace.Step($"less {deduction.Name}", -deduction.Amount.Amount);
        }

        context.Trace(trace.Result(context.NetPay.Value)
            .Explain("Net pay is gross earnings less every employee deduction. Employer " +
                     "contributions are not deducted from the employee and do not appear here.")
            .Build());
    }

    private static void ComputeEmployerCosts(CalculationContext context)
    {
        if (context.NssaEmployer is { } employer)
        {
            context.AddEmployerCost("NSSA_POBS_ER", "NSSA Employer (POBS)", employer,
                context.NssaInsurableEarnings ?? Money.Zero(context.Currency),
                context.Snapshot.Rules.Nssa?.EmployerRate, context.Snapshot.Rules.Nssa?.RuleId);
        }

        ComputeApwcs(context);
        ComputeLevies(context);

        if (context.GrossEarnings is { } gross && !context.HasUnresolved)
        {
            var employerTotal = Money.Sum(context.Currency,
                context.EmployerCosts.Select(c => c.Amount));
            context.TotalEmployerCost = gross + employerTotal;

            context.Trace(new TraceEntryBuilder("ComputeEmployerCosts", "Total employer cost")
                .Input("Gross earnings", gross)
                .Input("Employer contributions", employerTotal)
                .Result(context.TotalEmployerCost.Value)
                .Explain("What the employee actually costs: gross pay plus every employer-borne " +
                         "statutory contribution and levy.")
                .Build());
        }
    }

    private static void ComputeApwcs(CalculationContext context)
    {
        var rule = context.Snapshot.Rules.Apwcs;
        if (rule is null)
        {
            // The rate is assigned to this employer by NSSA; it cannot be inferred or averaged.
            context.Unresolve(context.Snapshot.Rules.FindUnresolved(StatutoryRuleType.Apwcs) ??
                new UnresolvedItem(UnresolvedCodes.ApwcsRuleUnresolved, "APWCS",
                    "No APWCS rate is configured for this company.",
                    StatutoryRuleType.Apwcs, null, null, "Q6",
                    "Obtain your assessed rate and industrial classification from NSSA (Form WC50) " +
                    "and enter them in Settings > APWCS Configuration."));
            return;
        }

        var baseAmount = rule.Base == ApwcsBase.GrossEarnings
            ? context.GrossEarnings ?? Money.Zero(context.Currency)
            : Money.Sum(context.Currency,
                context.Earnings.Where(l => l.Input.IsBasic).Select(l => l.Input.Amount));

        if (rule.CeilingAmount is { } ceiling)
        {
            baseAmount = Money.Min(baseAmount, new Money(ceiling, context.Currency));
        }

        var raw = baseAmount.Amount * rule.Rate;
        var amount = new Money(context.Rounding.Round(raw), context.Currency);
        context.Apwcs = amount;

        context.Trace(new TraceEntryBuilder("ComputeEmployerCosts", "APWCS")
            .FromRule(rule)
            .Input("Base", baseAmount)
            .Input("Industry code", rule.IndustryCode)
            .Step($"{baseAmount.Amount:N2} x {rule.Rate * 100m:0.####}%", raw)
            .Rounded(amount, context.Rounding.Describe())
            .Explain("Employer-only cost. Nothing is deducted from the employee.")
            .Build());

        context.AddEmployerCost("APWCS", "APWCS", amount, baseAmount, rule.Rate, rule.RuleId);
    }

    private static void ComputeLevies(CalculationContext context)
    {
        foreach (var rule in context.Snapshot.Rules.EmployerLevies.Where(r => r.IsActive))
        {
            var baseAmount = rule.Base switch
            {
                LevyBase.BasicEarnings => Money.Sum(context.Currency,
                    context.Earnings.Where(l => l.Input.IsBasic).Select(l => l.Input.Amount)),
                _ => Money.Sum(context.Currency,
                    context.Earnings.Where(l => l.Input.IsEmployerLevyBase)
                        .Select(l => l.Input.Amount))
            };

            var raw = baseAmount.Amount * rule.EmployerPortionRate;
            var amount = new Money(context.Rounding.Round(raw), context.Currency);

            context.Trace(new TraceEntryBuilder("ComputeEmployerCosts", rule.LevyType.ToString())
                .FromRule(rule)
                .Input("Base", baseAmount)
                .Step($"{baseAmount.Amount:N2} x {rule.EmployerPortionRate * 100m:0.####}%", raw)
                .Rounded(amount, context.Rounding.Describe())
                .Build());

            context.AddEmployerCost(rule.LevyType.ToString(), rule.Name, amount, baseAmount,
                rule.EmployerPortionRate, rule.RuleId);
        }
    }

    private static void AllocateCost(CalculationContext context)
    {
        if (context.TotalEmployerCost is not { } total)
        {
            return;
        }

        foreach (var allocation in context.Snapshot.CostAllocations)
        {
            var share = new Money(
                context.Rounding.Round(total.Amount * allocation.Percent / 100m), context.Currency);

            context.AddAllocation(new CostAllocationResult(
                allocation.ProjectId, allocation.ProjectName, allocation.DepartmentId,
                allocation.DepartmentName, allocation.Percent, share,
                allocation.ProjectSiteId, allocation.ProjectSiteName));
        }
    }
}
