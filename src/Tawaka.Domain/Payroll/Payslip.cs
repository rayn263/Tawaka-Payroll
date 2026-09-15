using Tawaka.Domain.Common;

namespace Tawaka.Domain.Payroll;

/// <summary>
/// A generated payslip document.
/// <para>
/// The payslip is a <b>record of issue</b>, not a second copy of the figures: the numbers live on
/// the payroll run, and the payslip points at them. Re-issuing after a correction creates a new
/// revision and marks the previous one superseded, so a payslip handed to an employee can always
/// be traced, and never silently disappears.
/// </para>
/// </summary>
public class Payslip : AuditableEntity
{
    public Guid PayrollRunEmployeeId { get; set; }

    public PayrollRunEmployee? PayrollRunEmployee { get; set; }

    /// <summary>Unique, human-quotable: PS-2026-09-0042.</summary>
    public string PayslipNumber { get; set; } = string.Empty;

    public int Revision { get; set; } = 1;

    public DateTimeOffset GeneratedAt { get; set; }

    public string GeneratedBy { get; set; } = string.Empty;

    /// <summary>
    /// A payslip from a development-mode run is watermarked and is not an issuable document.
    /// </summary>
    public bool IsDevelopmentCopy { get; set; }

    /// <summary>Set when the payslip was actually given to the employee.</summary>
    public DateTimeOffset? IssuedAt { get; set; }

    public string? IssuedBy { get; set; }

    public Guid? SupersededByPayslipId { get; set; }

    public string? SupersedeReason { get; set; }

    public string? FilePath { get; set; }

    /// <summary>Hash of the rendered document, so an altered copy can be detected.</summary>
    public string? ContentHash { get; set; }

    public string? TemplateVersion { get; set; }

    public bool IsSuperseded => SupersededByPayslipId is not null;

    public bool CanBeIssued => !IsDevelopmentCopy && !IsSuperseded;
}
