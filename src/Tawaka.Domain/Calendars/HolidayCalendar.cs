using Tawaka.Domain.Common;
using Tawaka.Domain.Statutory;

namespace Tawaka.Domain.Calendars;

public enum HolidayKind
{
    /// <summary>Declared a public holiday by law or by proclamation.</summary>
    PublicHoliday = 0,

    /// <summary>A holiday the company grants, such as a shutdown day.</summary>
    CompanyHoliday = 1,

    /// <summary>A day the company does not work, such as a site closure.</summary>
    NonWorkingDay = 2
}

/// <summary>
/// A company's working calendar for a span of time.
/// <para>
/// Public holidays are <b>data</b>, never code. Zimbabwe's public holidays are set by the Public
/// Holidays and Prohibition of Business Act and by presidential proclamation, and proclaimed days
/// change from year to year; a list compiled into the application would be wrong within a year and
/// silently so. Payroll reads the calendar that was recorded, and a payroll run keeps the calendar
/// it actually used (ADR-033).
/// </para>
/// </summary>
public class HolidayCalendar : AuditableEntity
{
    public Guid CompanyId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public DateOnly EffectiveFrom { get; set; }

    public DateOnly? EffectiveTo { get; set; }

    /// <summary>
    /// The calendar used where an employee, project or site names no other. Exactly one should be
    /// the default on any given date.
    /// </summary>
    public bool IsDefault { get; set; }

    public bool IsActive { get; set; } = true;

    public string? Notes { get; set; }

    public ICollection<PublicHoliday> Holidays { get; set; } = new List<PublicHoliday>();

    public DateRange EffectivePeriod => new(EffectiveFrom, EffectiveTo);

    public bool AppliesOn(DateOnly date) => IsActive && EffectivePeriod.Contains(date);
}

/// <summary>
/// One non-working day on a calendar, with its own evidence.
/// <para>
/// A holiday carries a verification status for the same reason a tax rule does: "Independence Day
/// falls on the 18th" is a claim about the law, and a payroll that pays holiday rates on the wrong
/// day is wrong in a way that costs money. A company holiday needs no such evidence, which is what
/// <see cref="HolidayKind"/> distinguishes.
/// </para>
/// </summary>
public class PublicHoliday : AuditableEntity
{
    public Guid HolidayCalendarId { get; set; }
    public HolidayCalendar? Calendar { get; set; }

    public DateOnly Date { get; set; }

    public string Name { get; set; } = string.Empty;

    public HolidayKind Kind { get; set; } = HolidayKind.PublicHoliday;

    /// <summary>
    /// Where a holiday falls on a Sunday and is observed on the Monday, this is the actual date
    /// and <see cref="Date"/> is the observed one, so both stay visible.
    /// </summary>
    public DateOnly? ActualDate { get; set; }

    public VerificationStatus VerificationStatus { get; set; } = VerificationStatus.Unverified;

    /// <summary>The proclamation, gazette or Act this date came from.</summary>
    public string? Source { get; set; }

    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// A company holiday is established by the company deciding it, so it needs no external
    /// verification. A public holiday is a claim about the law and does.
    /// </summary>
    public bool RequiresVerification => Kind == HolidayKind.PublicHoliday;
}
