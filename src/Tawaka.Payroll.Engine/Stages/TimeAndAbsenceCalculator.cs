using Tawaka.Domain.Common;
using Tawaka.Domain.Employees;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Statutory;
using Tawaka.Payroll.Engine.Inputs;
using Tawaka.Payroll.Engine.Results;
using Tawaka.Payroll.Engine.Tracing;

namespace Tawaka.Payroll.Engine.Stages;

/// <summary>
/// Turns approved time, absence and loan inputs into money.
/// <para>
/// This is the only place hours become pay. Timesheets, leave and loans record quantities and
/// approvals; the arithmetic that prices them lives here, in the pure engine, so there is exactly
/// one implementation of "what is an hour of Sunday overtime worth" and it is traced like every
/// other figure (ADR-031).
/// </para>
/// <para>
/// Nothing here assumes a rate. An overtime multiplier comes from a dated <see cref="OvertimeRule"/>
/// and a salary-to-hourly conversion from a dated <see cref="PayDivisorRule"/>; where either is
/// missing or has no value, the figure is left unresolved with its compliance question rather than
/// computed on a convention nobody has checked.
/// </para>
/// </summary>
internal static class TimeAndAbsenceCalculator
{
    public const string BasicTimeCode = "BASIC";
    public const string UnpaidLeaveCode = "UNPAID_LEAVE";

    /// <summary>
    /// Derives basic pay from approved time where the contract is paid by the hour or the day.
    /// Returns the earnings to add; an empty list where the basis is not time-based.
    /// </summary>
    public static IReadOnlyList<EarningInput> DeriveTimeBasedPay(
        PayrollInputSnapshot snapshot, Action<UnresolvedItem> unresolve, Action<TraceEntry> trace)
    {
        if (snapshot.EarningsBasis is not (EarningsBasis.HourlyRate or EarningsBasis.DailyRate))
        {
            return Array.Empty<EarningInput>();
        }

        if (snapshot.ContractRate is not { } rate)
        {
            unresolve(new UnresolvedItem(
                UnresolvedCodes.RateMissing, BasicTimeCode,
                "This employee is paid by the " +
                (snapshot.EarningsBasis == EarningsBasis.HourlyRate ? "hour" : "day") +
                ", but the contract carries no rate.",
                null, null, null, null,
                "Record the contract rate on the employee's current contract version."));
            return Array.Empty<EarningInput>();
        }

        // No approved timesheet means the quantity is unknown, which is not the same as zero. An
        // employee who worked but whose timesheet was never approved must not be paid nothing
        // silently; the run refuses and names them.
        if (snapshot.Timesheet is not { } timesheet)
        {
            unresolve(new UnresolvedItem(
                UnresolvedCodes.TimesheetMissing, BasicTimeCode,
                "This employee is paid for time worked, but no approved timesheet exists for this " +
                "period, so the hours or days worked are unknown.",
                null, null, null, null,
                "Capture the timesheet for this period and have it approved, then recalculate."));
            return Array.Empty<EarningInput>();
        }

        if (!timesheet.IsApproved)
        {
            unresolve(new UnresolvedItem(
                UnresolvedCodes.TimesheetNotApproved, BasicTimeCode,
                "A timesheet exists for this period but has not been approved, and payroll does " +
                "not consume unapproved input.",
                null, null, null, null,
                "Submit and approve the timesheet, then recalculate."));
            return Array.Empty<EarningInput>();
        }

        var quantity = snapshot.EarningsBasis == EarningsBasis.HourlyRate
            ? timesheet.HoursWorked
            : timesheet.DaysWorked;

        var unit = snapshot.EarningsBasis == EarningsBasis.HourlyRate ? "hours" : "days";
        var amount = new Money(rate.Amount * quantity, rate.Currency);

        trace(new TraceEntryBuilder("DeriveTimeBasedPay", BasicTimeCode)
            .Input("Approved " + unit, quantity.ToString("N2"))
            .Input("Contract rate", rate)
            .Step($"{quantity:N2} {unit} x {rate.Amount:N2} = {amount.Amount:N4}")
            .Result(amount)
            .Explain($"Basic pay for a {unit.TrimEnd('s')}-rated contract comes from approved time, " +
                     "never from an assumed full period.")
            .Build());

        return new[]
        {
            new EarningInput
            {
                Code = BasicTimeCode,
                Name = "Basic pay (time worked)",
                Amount = amount,
                Quantity = quantity,
                Rate = rate,
                IsBasic = true,
                IsTaxable = true,
                IsNssaApplicable = true,
                IsIncludedInGross = true,
                IsEmployerLevyBase = true,
                TreatmentVerification = VerificationStatus.Supported,
                Source = "Contract rate applied to approved time."
            }
        };
    }

    /// <summary>
    /// Prices each overtime category from its own dated rule. Categories are never merged: a
    /// Sunday hour and a weekday hour are different rules and stay different lines.
    /// </summary>
    public static IReadOnlyList<EarningInput> DeriveOvertime(
        PayrollInputSnapshot snapshot, Action<UnresolvedItem> unresolve, Action<TraceEntry> trace)
    {
        if (snapshot.Timesheet is not { IsApproved: true } timesheet ||
            timesheet.Overtime.Count == 0)
        {
            return Array.Empty<EarningInput>();
        }

        var earnings = new List<EarningInput>();

        foreach (var overtime in timesheet.Overtime.Where(o => o.Hours != 0m))
        {
            if (overtime.Multiplier is not { } multiplier || multiplier <= 0m)
            {
                unresolve(new UnresolvedItem(
                    UnresolvedCodes.OvertimeMultiplierUnresolved, overtime.CategoryCode,
                    $"{overtime.Hours:N2} hours of '{overtime.CategoryName}' were approved, but the " +
                    "overtime rate for this category has not been established.",
                    StatutoryRuleType.Overtime, overtime.RuleId, overtime.RuleVerification, "Q31",
                    "Obtain the overtime multiplier from the Labour Act or the applicable NEC " +
                    "collective bargaining agreement, record it with its source, and verify the rule."));
                continue;
            }

            var hourly = OrdinaryHourlyRate(snapshot, unresolve, trace, overtime.CategoryCode);
            if (hourly is not { } hourlyRate)
            {
                continue;
            }

            var amount = new Money(hourlyRate.Amount * overtime.Hours * multiplier, hourlyRate.Currency);

            var entry = new TraceEntryBuilder("DeriveOvertime", overtime.CategoryCode)
                .Input("Approved hours", overtime.Hours.ToString("N2"))
                .Input("Ordinary hourly rate", hourlyRate)
                .Input("Multiplier", multiplier.ToString("N2"))
                .Step($"{overtime.Hours:N2} x {hourlyRate.Amount:N4} x {multiplier:N2} = {amount.Amount:N4}")
                .Result(amount)
                .Explain("Each overtime category is priced by its own dated rule; categories are " +
                         "never combined into a single rate.");

            var rule = snapshot.Rules.Overtime
                .FirstOrDefault(r => r.CategoryCode == overtime.CategoryCode);
            if (rule is not null)
            {
                entry = entry.FromRule(rule);
            }

            trace(entry.Build());

            earnings.Add(new EarningInput
            {
                Code = overtime.CategoryCode,
                Name = overtime.CategoryName,
                Amount = amount,
                Quantity = overtime.Hours,
                Rate = hourlyRate,
                IsOvertime = true,
                IsTaxable = overtime.IsTaxable,
                IsNssaApplicable = overtime.IsNssaApplicable,
                IsIncludedInGross = true,
                IsEmployerLevyBase = true,
                TreatmentVerification = overtime.RuleVerification,
                Source = overtime.Source
            });
        }

        return earnings;
    }

    /// <summary>
    /// Deducts unpaid leave. Paid leave produces no line at all: the employee is paid as normal,
    /// and inventing an offsetting pair of lines would only make the payslip harder to read.
    /// </summary>
    public static IReadOnlyList<DeductionInput> DeriveUnpaidLeave(
        PayrollInputSnapshot snapshot, Action<UnresolvedItem> unresolve, Action<TraceEntry> trace)
    {
        var unpaidDays = snapshot.LeaveEffects.Where(l => !l.IsPaid).Sum(l => l.Days);
        if (unpaidDays <= 0m)
        {
            return Array.Empty<DeductionInput>();
        }

        var daily = OrdinaryDailyRate(snapshot, unresolve, trace, UnpaidLeaveCode);
        if (daily is not { } dailyRate)
        {
            return Array.Empty<DeductionInput>();
        }

        var amount = new Money(dailyRate.Amount * unpaidDays, dailyRate.Currency);

        trace(new TraceEntryBuilder("DeriveUnpaidLeave", UnpaidLeaveCode)
            .Input("Approved unpaid days", unpaidDays.ToString("N2"))
            .Input("Daily rate", dailyRate)
            .Step($"{unpaidDays:N2} days x {dailyRate.Amount:N4} = {amount.Amount:N4}")
            .Result(amount)
            .Explain("Unpaid leave reduces pay by the daily rate for each approved unpaid day.")
            .Build());

        return new[]
        {
            new DeductionInput
            {
                Code = UnpaidLeaveCode,
                Name = "Unpaid leave",
                Amount = amount,

                // Unpaid leave is not a deduction from pay in the tax sense: the employee simply
                // earned less. Reducing taxable income is therefore correct and is what makes the
                // PAYE figure right.
                ReducesTaxableIncome = true,
                AppliesBeforeTax = true,
                Priority = 1,
                TreatmentVerification = VerificationStatus.Supported
            }
        };
    }

    /// <summary>
    /// Turns approved loan recoveries into post-tax deductions. The amount and its cap against the
    /// outstanding balance were decided by the loan ledger before the snapshot was built; this
    /// stage only places them in the calculation and traces what they were capped against.
    /// </summary>
    public static IReadOnlyList<DeductionInput> DeriveLoanDeductions(
        PayrollInputSnapshot snapshot, Action<TraceEntry> trace)
    {
        if (snapshot.LoanDeductions.Count == 0)
        {
            return Array.Empty<DeductionInput>();
        }

        var deductions = new List<DeductionInput>();

        foreach (var loan in snapshot.LoanDeductions.Where(l => l.Amount.Amount != 0m))
        {
            trace(new TraceEntryBuilder("DeriveLoanDeductions", loan.LoanNumber)
                .Input("Outstanding before this deduction", loan.OutstandingBefore)
                .Input("Instalment", loan.Amount)
                .Result(loan.Amount)
                .Explain(loan.WasCappedAtOutstanding
                    ? "Reduced to the outstanding balance: a repayment may not exceed what is owed."
                    : loan.OverRecoveryApproved
                        ? "Over-recovery was explicitly approved for this loan."
                        : "Scheduled instalment, within the outstanding balance.")
                .Build());

            deductions.Add(new DeductionInput
            {
                Code = loan.LoanTypeCode,
                Name = $"{loan.LoanTypeCode} {loan.LoanNumber}",
                Amount = loan.Amount,
                ReducesTaxableIncome = false,
                AppliesBeforeTax = false,
                Priority = 200,
                TreatmentVerification = VerificationStatus.Supported
            });
        }

        return deductions;
    }

    /// <summary>
    /// The ordinary hourly rate. Direct for an hourly contract; for a salary it needs the divisor
    /// convention, which is a rule, not an assumption.
    /// </summary>
    private static Money? OrdinaryHourlyRate(
        PayrollInputSnapshot snapshot, Action<UnresolvedItem> unresolve, Action<TraceEntry> trace,
        string itemKey)
    {
        if (snapshot.ContractRate is not { } rate)
        {
            unresolve(new UnresolvedItem(
                UnresolvedCodes.RateMissing, itemKey,
                "The contract carries no rate, so an hourly rate cannot be established.",
                null, null, null, null,
                "Record the contract rate on the employee's current contract version."));
            return null;
        }

        switch (snapshot.EarningsBasis)
        {
            case EarningsBasis.HourlyRate:
                return rate;

            case EarningsBasis.DailyRate when snapshot.StandardHoursPerDay > 0m:
                return new Money(rate.Amount / snapshot.StandardHoursPerDay, rate.Currency);

            default:
                return DivideSalary(snapshot, unresolve, trace, itemKey, hourly: true);
        }
    }

    /// <summary>The ordinary daily rate, on the same terms as the hourly one.</summary>
    private static Money? OrdinaryDailyRate(
        PayrollInputSnapshot snapshot, Action<UnresolvedItem> unresolve, Action<TraceEntry> trace,
        string itemKey)
    {
        if (snapshot.ContractRate is not { } rate)
        {
            unresolve(new UnresolvedItem(
                UnresolvedCodes.RateMissing, itemKey,
                "The contract carries no rate, so a daily rate cannot be established.",
                null, null, null, null,
                "Record the contract rate on the employee's current contract version."));
            return null;
        }

        return snapshot.EarningsBasis switch
        {
            EarningsBasis.DailyRate => rate,
            EarningsBasis.HourlyRate when snapshot.StandardHoursPerDay > 0m =>
                new Money(rate.Amount * snapshot.StandardHoursPerDay, rate.Currency),
            _ => DivideSalary(snapshot, unresolve, trace, itemKey, hourly: false)
        };
    }

    private static Money? DivideSalary(
        PayrollInputSnapshot snapshot, Action<UnresolvedItem> unresolve, Action<TraceEntry> trace,
        string itemKey, bool hourly)
    {
        var rate = snapshot.ContractRate!.Value;
        var divisorRule = snapshot.Rules.PayDivisor;
        var divisor = hourly ? divisorRule?.HoursInPeriod : divisorRule?.DaysInPeriod;

        if (divisorRule is null || divisor is not { } value || value <= 0m)
        {
            unresolve(new UnresolvedItem(
                UnresolvedCodes.OvertimeRuleUnresolved, itemKey,
                $"Converting a {snapshot.PaymentFrequency.ToString().ToLowerInvariant()} salary to " +
                (hourly ? "an hourly" : "a daily") + " rate needs a divisor convention " +
                (hourly ? "(ordinary hours per period)" : "(working days per period)") +
                ", and none has been established.",
                StatutoryRuleType.PayDivisor, divisorRule?.RuleId, divisorRule?.VerificationStatus,
                "Q33",
                "Establish the working-days and ordinary-hours convention from the Labour Act or " +
                "the applicable NEC agreement, record it with its source, and verify the rule."));
            return null;
        }

        var derived = new Money(rate.Amount / value, rate.Currency);

        trace(new TraceEntryBuilder("DivideSalary", itemKey)
            .FromRule(divisorRule)
            .Input("Salary", rate)
            .Input(hourly ? "Ordinary hours in period" : "Working days in period", value.ToString("N2"))
            .Step($"{rate.Amount:N2} / {value:N2} = {derived.Amount:N4}")
            .Result(derived)
            .Explain("The divisor is a dated rule, not an arithmetic convention chosen here.")
            .Build());

        return derived;
    }
}
