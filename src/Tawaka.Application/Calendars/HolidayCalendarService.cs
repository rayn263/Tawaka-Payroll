using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Abstractions;
using Tawaka.Application.Common;
using Tawaka.Application.Security;
using Tawaka.Domain.Calendars;
using Tawaka.Domain.Security;
using Tawaka.Domain.Statutory;

namespace Tawaka.Application.Calendars;

public sealed record HolidayCommand
{
    public required DateOnly Date { get; init; }
    public required string Name { get; init; }
    public HolidayKind Kind { get; init; } = HolidayKind.PublicHoliday;
    public DateOnly? ActualDate { get; init; }
    public string? Source { get; init; }
    public string? Notes { get; init; }
}

/// <summary>
/// The company's working calendar.
/// <para>
/// No public holiday is shipped in code. Zimbabwe's holidays are set by Act and by presidential
/// proclamation and change from year to year; a compiled-in list would be silently wrong within a
/// year, and payroll would price a holiday that was not one. The calendar is captured, sourced and
/// graded, and a payroll run records the calendar it used (ADR-033).
/// </para>
/// </summary>
public sealed class HolidayCalendarService
{
    private readonly IPayrollDataContext _context;
    private readonly ICurrentUser _currentUser;

    public HolidayCalendarService(IPayrollDataContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<OperationResult<HolidayCalendar>> CreateCalendarAsync(
        Guid companyId, string code, string name, DateOnly effectiveFrom, DateOnly? effectiveTo,
        bool isDefault, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.CalendarEdit);

        var validation = ValidationResult.Success();
        validation.Require(code, "Code");
        validation.Require(name, "Name");
        validation.AddIf(effectiveTo is { } to && to < effectiveFrom, "EffectiveTo",
            "A calendar cannot end before it starts.");

        var duplicate = await _context.HolidayCalendars.AsNoTracking()
            .AnyAsync(c => c.CompanyId == companyId && c.Code == code, cancellationToken)
            .ConfigureAwait(false);
        validation.AddIf(duplicate, "Code", $"Calendar '{code}' already exists.");

        if (!validation.IsValid)
        {
            return OperationResult<HolidayCalendar>.Failed(validation);
        }

        // Exactly one default at a time, so "which calendar applied" has one answer.
        if (isDefault)
        {
            var others = await _context.HolidayCalendars
                .Where(c => c.CompanyId == companyId && c.IsDefault)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var other in others)
            {
                other.IsDefault = false;
            }
        }

        var calendar = new HolidayCalendar
        {
            CompanyId = companyId,
            Code = code,
            Name = name,
            EffectiveFrom = effectiveFrom,
            EffectiveTo = effectiveTo,
            IsDefault = isDefault
        };

        _context.HolidayCalendars.Add(calendar);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult<HolidayCalendar>.Success(calendar);
    }

    public async Task<OperationResult<PublicHoliday>> AddHolidayAsync(
        Guid calendarId, HolidayCommand command, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.CalendarEdit);

        var validation = ValidationResult.Success();
        validation.Require(command.Name, "Name");

        var calendar = await _context.HolidayCalendars.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == calendarId, cancellationToken).ConfigureAwait(false);

        if (calendar is null)
        {
            return OperationResult<PublicHoliday>.Failed(
                validation.Add("Calendar", "Holiday calendar not found."));
        }

        validation.AddIf(!calendar.EffectivePeriod.Contains(command.Date), "Date",
            $"{command.Date:dd MMM yyyy} falls outside this calendar's effective period.");

        var duplicate = await _context.PublicHolidays.AsNoTracking()
            .AnyAsync(h => h.HolidayCalendarId == calendarId && h.Date == command.Date &&
                           h.IsActive, cancellationToken)
            .ConfigureAwait(false);
        validation.AddIf(duplicate, "Date",
            $"{command.Date:dd MMM yyyy} is already a holiday on this calendar.");

        if (!validation.IsValid)
        {
            return OperationResult<PublicHoliday>.Failed(validation);
        }

        var holiday = new PublicHoliday
        {
            HolidayCalendarId = calendarId,
            Date = command.Date,
            Name = command.Name,
            Kind = command.Kind,
            ActualDate = command.ActualDate,
            Source = command.Source,
            Notes = command.Notes,

            // A company holiday is established by the company saying so. A public holiday is a
            // claim about the law, and starts unverified until somebody cites the proclamation.
            VerificationStatus = command.Kind == HolidayKind.PublicHoliday
                ? VerificationStatus.Unverified
                : VerificationStatus.Verified
        };

        _context.PublicHolidays.Add(holiday);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult<PublicHoliday>.Success(holiday);
    }

    /// <summary>
    /// Records that a public holiday has been confirmed against its proclamation or Act. Requires
    /// the same permission as verifying a statutory rule, and the same thing: a citation.
    /// </summary>
    public async Task<ValidationResult> VerifyHolidayAsync(
        Guid holidayId, string source, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.StatutoryVerifyRules);

        var validation = ValidationResult.Success();
        validation.Require(source, "Source",
            "Verifying a public holiday requires the proclamation or Act it comes from.");

        var holiday = await _context.PublicHolidays
            .FirstOrDefaultAsync(h => h.Id == holidayId, cancellationToken).ConfigureAwait(false);

        if (holiday is null)
        {
            return validation.Add("Holiday", "Holiday not found.");
        }

        if (!validation.IsValid)
        {
            return validation;
        }

        holiday.VerificationStatus = VerificationStatus.Verified;
        holiday.Source = source;
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return validation;
    }

    public async Task<ValidationResult> RemoveHolidayAsync(
        Guid holidayId, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.CalendarEdit);

        var validation = ValidationResult.Success();
        var holiday = await _context.PublicHolidays
            .FirstOrDefaultAsync(h => h.Id == holidayId, cancellationToken).ConfigureAwait(false);

        if (holiday is null)
        {
            return validation.Add("Holiday", "Holiday not found.");
        }

        // Deactivated rather than deleted: a payroll run may have priced a day against it, and the
        // run has to stay explicable.
        holiday.IsActive = false;
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return validation;
    }

    public Task<List<HolidayCalendar>> GetCalendarsAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.CalendarView);

        return _context.HolidayCalendars.AsNoTracking()
            .Include(c => c.Holidays)
            .Where(c => c.CompanyId == companyId)
            .OrderByDescending(c => c.EffectiveFrom)
            .ToListAsync(cancellationToken);
    }

    public Task<HolidayCalendar?> GetDefaultAsync(
        Guid companyId, DateOnly on, CancellationToken cancellationToken = default) =>
        _context.HolidayCalendars.AsNoTracking()
            .Include(c => c.Holidays)
            .Where(c => c.CompanyId == companyId && c.IsActive && c.IsDefault &&
                        c.EffectiveFrom <= on && (c.EffectiveTo == null || c.EffectiveTo >= on))
            .FirstOrDefaultAsync(cancellationToken);
}
