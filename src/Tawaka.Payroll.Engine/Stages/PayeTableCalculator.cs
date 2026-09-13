using Tawaka.Domain.Common;
using Tawaka.Domain.Statutory;
using Tawaka.Payroll.Engine.Results;
using Tawaka.Payroll.Engine.Rounding;
using Tawaka.Payroll.Engine.Tracing;

namespace Tawaka.Payroll.Engine.Stages;

/// <summary>The outcome of applying a tax table: either an amount, or a reason it could not be applied.</summary>
public sealed record PayeComputation(Money? Tax, UnresolvedItem? Unresolved, TraceEntry Trace);

/// <summary>
/// Applies a PAYE table to taxable income.
/// <para>
/// Two table forms are supported, because ZIMRA publishes its tables in
/// "gross x rate - fixed deduction" form while the bands themselves describe a progressive
/// ladder. Both give the same answer when the deduction column is correct; the published form is
/// preferred so results reconcile line-for-line with the authority's own figures.
/// </para>
/// <para>
/// If the table declares the published form but its fixed-deduction column has not been captured,
/// the calculator <b>refuses</b> rather than silently falling back to the ladder — that column is
/// compliance question Q26, and a fallback would hide it.
/// </para>
/// </summary>
public sealed class PayeTableCalculator
{
    private readonly RoundingPolicy _rounding;

    public PayeTableCalculator(RoundingPolicy rounding) => _rounding = rounding;

    public PayeComputation Compute(TaxRule table, Money taxableIncome)
    {
        ArgumentNullException.ThrowIfNull(table);

        var trace = new TraceEntryBuilder("ComputePaye", "PAYE")
            .FromRule(table)
            .Input("Taxable income", taxableIncome)
            .Input("Period basis", table.PeriodBasis.ToString())
            .Input("Table form", table.BracketApplication.ToString());

        var brackets = table.OrderedBrackets();
        if (brackets.Count == 0)
        {
            return Unresolved(table, trace, UnresolvedCodes.PayeTableUnresolved,
                $"PAYE table '{table.RuleId}' has no bands configured.",
                "Load the official table for this currency and pay frequency.");
        }

        return table.BracketApplication switch
        {
            TaxBracketApplication.RateLessFixedDeduction =>
                ComputePublishedForm(table, brackets, taxableIncome, trace),
            _ => ComputeProgressiveLadder(table, brackets, taxableIncome, trace)
        };
    }

    /// <summary>Sums each band's own slice — the form the seeded bands describe.</summary>
    private PayeComputation ComputeProgressiveLadder(
        TaxRule table, IReadOnlyList<TaxBracket> brackets, Money taxableIncome,
        TraceEntryBuilder trace)
    {
        var income = taxableIncome.Amount;
        var total = 0m;

        foreach (var bracket in brackets)
        {
            if (income <= bracket.LowerBound)
            {
                continue;
            }

            var upper = bracket.UpperBound ?? income;
            var slice = Math.Min(income, upper) - bracket.LowerBound;
            if (slice <= 0m)
            {
                continue;
            }

            var bandTax = slice * bracket.Rate;
            if (_rounding.RoundIntermediateTaxSteps)
            {
                bandTax = _rounding.Round(bandTax);
            }

            total += bandTax;
            trace.Step(
                $"{slice.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)} x " +
                $"{bracket.Rate * 100m:0.##}%", bandTax);
        }

        trace.Raw(total);
        var rounded = new Money(_rounding.Round(total), taxableIncome.Currency);
        trace.Rounded(rounded, _rounding.Describe())
            .Explain("Progressive table: each band applied to its own slice of taxable income.");

        return new PayeComputation(rounded, null, trace.Build());
    }

    /// <summary>Applies the published "income x rate less fixed deduction" form.</summary>
    private PayeComputation ComputePublishedForm(
        TaxRule table, IReadOnlyList<TaxBracket> brackets, Money taxableIncome,
        TraceEntryBuilder trace)
    {
        var income = taxableIncome.Amount;
        var bracket = brackets.FirstOrDefault(b => b.Contains(income))
                      ?? brackets.First(b => income <= b.LowerBound || b.UpperBound is null);

        if (bracket.FixedDeduction is null)
        {
            return Unresolved(table, trace, UnresolvedCodes.PayeFixedDeductionUnresolved,
                $"PAYE table '{table.RuleId}' is published in 'income x rate less fixed deduction' " +
                $"form, but the fixed-deduction value for the band starting at {bracket.LowerBound} " +
                "has not been captured.",
                "Read the official table and enter the 'less' column for every band.",
                "Q26");
        }

        var raw = income * bracket.Rate - bracket.FixedDeduction.Value;
        if (raw < 0m)
        {
            raw = 0m;
        }

        trace.Input("Band", $"{bracket.LowerBound} to {(object?)bracket.UpperBound ?? "above"}")
            .Step($"{income:N2} x {bracket.Rate * 100m:0.##}%", income * bracket.Rate)
            .Step($"less fixed deduction {bracket.FixedDeduction.Value:N2}", raw)
            .Raw(raw);

        var rounded = new Money(_rounding.Round(raw), taxableIncome.Currency);
        trace.Rounded(rounded, _rounding.Describe())
            .Explain("Published table form: taxable income x band rate, less the band's fixed deduction.");

        return new PayeComputation(rounded, null, trace.Build());
    }

    private static PayeComputation Unresolved(
        TaxRule table, TraceEntryBuilder trace, string code, string message, string remedy,
        string? question = null)
    {
        trace.Explain(message);
        return new PayeComputation(
            null,
            new UnresolvedItem(code, "PAYE", message, StatutoryRuleType.PayeTable, table.RuleId,
                table.VerificationStatus, question, remedy),
            trace.Build());
    }
}
