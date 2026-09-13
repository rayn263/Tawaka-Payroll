namespace Tawaka.Payroll.Engine.Rounding;

/// <summary>
/// The single rounding policy for the whole engine.
/// <para>
/// No calculation class chooses its own rounding. Every stage rounds through this policy, and
/// every rounding it applies is recorded in the trace, so a half-cent difference is explainable
/// rather than mysterious.
/// </para>
/// <para>
/// Defaults, and why: two decimal places, away from zero. Away-from-zero is used rather than
/// banker's rounding because a payroll that rounds half-to-even drifts systematically in the
/// employer's favour across a workforce, and because published tax tables are computed that way.
/// Intermediate tax steps are <b>not</b> rounded by default: rounding each band before summing
/// introduces error that ZIMRA's own tables do not contain. Whether the official tables require
/// intermediate rounding is unconfirmed (compliance spec Q26), so it is configurable rather than
/// assumed.
/// </para>
/// </summary>
public sealed record RoundingPolicy
{
    public static readonly RoundingPolicy Default = new();

    /// <summary>Decimal places for money results. Overridden per currency where one differs.</summary>
    public int Precision { get; init; } = 2;

    public MidpointRounding Mode { get; init; } = MidpointRounding.AwayFromZero;

    /// <summary>
    /// Whether each band of a progressive tax table is rounded before the bands are summed.
    /// False by default: the table is computed at full precision and the total rounded once.
    /// </summary>
    public bool RoundIntermediateTaxSteps { get; init; }

    /// <summary>
    /// Whether statutory contributions (NSSA, levies) are rounded when computed, rather than only
    /// at the payslip. True, because the amount deducted from an employee must be a real cash
    /// amount, and because it is what is remitted.
    /// </summary>
    public bool RoundStatutoryContributions { get; init; } = true;

    /// <summary>
    /// Whether the final payslip figures are rounded again after aggregation. False: the
    /// components are already rounded, and re-rounding a sum of rounded values can only introduce
    /// a discrepancy between the payslip total and the sum of its lines.
    /// </summary>
    public bool RoundPayslipTotalsSeparately { get; init; }

    public decimal Round(decimal value) => decimal.Round(value, Precision, Mode);

    public Domain.Common.Money Round(Domain.Common.Money value) =>
        new(Round(value.Amount), value.Currency);

    /// <summary>A human-readable description of what was applied, for the trace.</summary>
    public string Describe() =>
        $"{Precision} decimal places, {(Mode == MidpointRounding.AwayFromZero ? "away from zero" : Mode.ToString())}";
}
